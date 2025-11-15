using Agenty.AgentCore.Steps;
using Agenty.AgentCore.Runtime;
using Agenty.AgentCore.TokenHandling;
using Agenty.LLMCore;
using Agenty.LLMCore.ChatHandling;
using Agenty.LLMCore.Messages;
using Agenty.LLMCore.Providers.OpenAI;
using Agenty.LLMCore.ToolHandling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Agenty.AgentCore
{
    // === 1. Context (like HttpContext) ===
    public interface IAgentContext
    {
        Conversation Chat { get; }
        string? UserRequest { get; set; }
        AgentResponse Response { get; }
        IServiceProvider Services { get; }
        IDictionary<string, object?> Items { get; }
        Action<string>? Stream { get; set; }
        CancellationToken CancellationToken { get; }
    }

    public sealed class AgentResponse
    {
        public string? Message { get; private set; }
        public object? Payload { get; private set; }

        public void Set(string? message = null, object? payload = null)
        {
            Message = message;
            Payload = payload;
        }
    }

    internal sealed class AgentContext : IAgentContext
    {
        private readonly AgentResponse _response = new AgentResponse();

        public AgentContext(IServiceProvider services, CancellationToken cancellationToken = default)
        {
            Services = services;
            CancellationToken = cancellationToken;
            Chat = new Conversation();
            Items = new Dictionary<string, object?>();
        }

        public Conversation Chat { get; }
        public string? UserRequest { get; set; }
        public AgentResponse Response => _response;
        public IServiceProvider Services { get; }
        public IDictionary<string, object?> Items { get; }
        public Action<string>? Stream { get; set; }
        public CancellationToken CancellationToken { get; }
        public void SetResult(string? message, object? payload) => _response.Set(message, payload);
    }

    // === 2. AgentBuilder (like WebApplicationBuilder) ===
    public sealed class AgentBuilder
    {
        public IServiceCollection Services { get; } = new ServiceCollection();

        public AgentBuilder()
        {
            Services.AddSingleton<IConversationStore, FileConversationStore>();
            Services.AddSingleton<ITokenizer, SharpTokenTokenizer>();
            Services.AddSingleton<IContextTrimmer, SlidingWindowTrimmer>();
            Services.AddSingleton<ToolRegistryCatalog>();
            Services.AddSingleton<IToolRegistry>(sp => sp.GetRequiredService<ToolRegistryCatalog>());
            Services.AddSingleton<IToolCatalog>(sp => sp.GetRequiredService<ToolRegistryCatalog>());
            Services.AddSingleton<IToolRuntime, ToolRuntime>();
            Services.AddSingleton<ITokenManager, TokenManager>();
            Services.AddSingleton<IRetryPolicy, DefaultRetryPolicy>();
            Services.Configure<RetryPolicyOptions>(_ => { }); // default values
            Services.AddLogging(builder =>
            {
                builder.AddSimpleConsole(options =>
                {
                    options.SingleLine = false;
                    options.TimestampFormat = "hh:mm:ss ";
                    options.UseUtcTimestamp = false;
                    options.IncludeScopes = false;
                });
            });
        }

        public AgentBuilder WithLogLevel(LogLevel level)
        {
            Services.Configure<LoggerFilterOptions>(opts => opts.MinLevel = level);
            return this;
        }

        public Agent Build()
        {
            var provider = Services.BuildServiceProvider(validateScopes: true);
            var hasLLM = provider.GetService<ILLMClient>() != null;

            if (!hasLLM)
                Console.WriteLine("[Warning] No LLM client/coordinator registered. Tool calls or planning may fail.");

            return new Agent(provider);
        }
    }

    // === 3. Agent (like WebApplication) ===
    public sealed class Agent : IAgent
    {
        private readonly IServiceProvider _services;
        private readonly Conversation _history = new Conversation();
        private readonly IConversationStore? _store;

        private int _maxContextTokens = 8000;
        private AgentStepDelegate _pipeline = ctx => Task.CompletedTask;
        public IServiceProvider Services => _services;
        public Conversation ChatHistory => _history;

        internal Agent(IServiceProvider services)
        {
            _services = services;
            _store = services.GetService<IConversationStore>();
        }

        // Configure runtime (like app.MapXyz / app.UseXyz)
        public Agent WithTools<T>(params string[] tags)
        {
            _services.GetRequiredService<IToolRegistry>().RegisterAll<T>(tags);
            return this;
        }

        public Agent WithTools<T>(T instance, params string[] tags)
        {
            _services.GetRequiredService<IToolRegistry>().RegisterAll(instance, tags);
            return this;
        }

        public Agent WithSystemPrompt(string prompt)
        {
            _history.AddSystem(prompt);
            return this;
        }

        public Agent WithMaxContextWindow(int maxTokens)
        {
            _maxContextTokens = maxTokens;
            return this;
        }

        // === Load/save conversation history explicitly ===
        public async Task LoadHistoryAsync(string sessionId)
        {
            if (_store == null) return;
            var past = await _store.LoadAsync(sessionId);
            if (past != null)
                _history.CloneFrom(past);
        }

        public async Task SaveHistoryAsync(string sessionId)
        {
            if (_store == null) return;
            await _store.SaveAsync(sessionId, _history);
        }
        // app.Use(...)
        public Agent Use(Func<IAgentContext, AgentStepDelegate, Task> middleware)
        {
            var next = _pipeline;
            _pipeline = ctx => middleware(ctx, next);
            return this;
        }
        // default overload — supports DI and optional constructors 
        // unified Use<TStep> — handles both DI and factory forms
        private int _runningTotalTokens = 0;

        public Agent Use<TStep>(Func<TStep>? factory = null) where TStep : IAgentStep
        {
            return Use(async (ctx, next) =>
            {
                var logger = ctx.Services.GetService<ILogger<Agent>>();
                var tokenMgr = ctx.Services.GetService<ITokenManager>();
                var stepName = typeof(TStep).Name;

                var before = tokenMgr?.GetTotals() ?? TokenUsage.Empty;
                var sw = Stopwatch.StartNew();

                var prev = StepContext.Current.Value;
                StepContext.Current.Value = stepName;

                var step = factory != null
                    ? factory()
                    : (TStep)ActivatorUtilities.CreateInstance(ctx.Services, typeof(TStep));

                await step.InvokeAsync(ctx, async innerCtx =>
                {
                    sw.Stop();
                    var after = tokenMgr?.GetTotals() ?? TokenUsage.Empty;
                    var delta = after - before;
                    logger?.LogInformation(
                        "│ Step: {Step,-20} │ Time: {Ms,6} ms │ In: {In,5} │ Out: {Out,5} │ Δ: {Delta,5} │ Total: {Total,6} │",
                        stepName,
                        sw.ElapsedMilliseconds,
                        delta.InputTokens,
                        delta.OutputTokens,
                        delta.Total,
                        after.Total);

                    await next(innerCtx);
                });

                StepContext.Current.Value = prev;
            });
        }

        // run
        public async Task<AgentResponse> ExecuteAsync(string goal, CancellationToken ct = default)
        {
            using (var scope = _services.CreateScope())
            {
                var ctx = new AgentContext(scope.ServiceProvider, ct) { UserRequest = goal };

                try
                {
                    _history.AddUser(goal);
                    ctx.Chat.CloneFrom(_history);

                    var logger = ctx.Services.GetService<ILogger<Agent>>();
                    logger.AttachTo(ctx.Chat);

                    var ctxTrimmer = ctx.Services.GetService<IContextTrimmer>();
                    if (ctxTrimmer != null)
                    {
                        var usable = (int)(_maxContextTokens * 0.6);
                        var beforeCount = ctx.Chat.Count;
                        ctxTrimmer.Trim(ctx.Chat, usable);
                        var afterCount = ctx.Chat.Count;

                        if (afterCount < beforeCount)
                            logger?.LogWarning("Context trimmed from {Before} to {After} messages (usable: {Usable} tokens)", beforeCount, afterCount, usable);
                    }

                    await _pipeline(ctx);

                    if (!string.IsNullOrWhiteSpace(ctx.Response.Message))
                        _history.AddAssistant(ctx.Response.Message);
                }
                catch (Exception ex)
                {
                    var logger = ctx.Services.GetService<ILogger<Agent>>();
                    if (logger != null)
                        logger.LogError(ex, "Agent execution failed");

                    ctx.SetResult("Error: " + ex.Message, null);
                }

                return ctx.Response;
            }
        }

        public static AgentBuilder CreateBuilder() => new AgentBuilder();
    }

    public interface IAgent
    {
        Task<AgentResponse> ExecuteAsync(string goal, CancellationToken cancellationToken = default);
    }

    public sealed class OpenAIOptions
    {
        public string BaseUrl { get; set; } = "http://127.0.0.1:1234/v1";
        public string ApiKey { get; set; } = "lmstudio";
        public string Model { get; set; } = "qwen@q5_k_m";
    }

    public static class AgentServiceCollectionExtensions
    {
        public static AgentBuilder AddOpenAI(this AgentBuilder builder, Action<OpenAIOptions> configure)
        {
            var options = new OpenAIOptions();
            configure(options);

            builder.Services.AddSingleton<IToolRuntime>(sp =>
            {
                var registry = sp.GetRequiredService<IToolCatalog>();
                var logger = sp.GetService<ILogger<ToolRuntime>>();
                return new ToolRuntime(registry, logger);
            });

            builder.Services.AddSingleton<ILLMClient>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<ILLMClient>>();
                var registry = sp.GetRequiredService<IToolCatalog>();
                var runtime = sp.GetRequiredService<IToolRuntime>();
                var tokenManager = sp.GetRequiredService<ITokenManager>();
                var parser = new ToolCallParser();
                var retry = sp.GetRequiredService<IRetryPolicy>();
                return new OpenAILLMClient(options.BaseUrl, options.ApiKey, options.Model, registry, runtime, parser, tokenManager, retry, logger);
            });

            return builder;
        }

        public static AgentBuilder AddRetryPolicy(this AgentBuilder builder, Action<RetryPolicyOptions>? configure = null)
        {
            if (configure != null)
                builder.Services.Configure(configure);
            else
                builder.Services.Configure<RetryPolicyOptions>(_ => { }); // defaults

            builder.Services.AddSingleton<IRetryPolicy, DefaultRetryPolicy>();
            return builder;
        }
    }
    public static class AgentContextStreamingExtensions
    {
        private const string TextBufferKey = "__stream_text_buffer";
        private const string ToolCallKey = "__stream_tool_call";

        public static void StreamBegin(this IAgentContext ctx)
        {
            ctx.Items[TextBufferKey] = new StringBuilder();
            ctx.Items[ToolCallKey] = null;
        }

        public static void StreamApply(this IAgentContext ctx, LLMStreamChunk chunk)
        {
            switch (chunk.Kind)
            {
                case StreamKind.Text:
                    ctx.Stream?.Invoke(chunk.AsText()!);
                    ((StringBuilder)ctx.Items[TextBufferKey]).Append(chunk.AsText());
                    break;

                case StreamKind.ToolCall:
                    ctx.Items[ToolCallKey] = chunk.AsToolCall();
                    break;
            }
        }

        public static string StreamFinalText(this IAgentContext ctx)
            => ctx.Items[TextBufferKey] is StringBuilder sb ? sb.ToString().Trim() : "";

        public static ToolCall? StreamFinalToolCall(this IAgentContext ctx)
            => ctx.Items[ToolCallKey] as ToolCall;
    }


    /// <summary>
    /// Tracks the current step name using AsyncLocal for implicit context flow.
    /// Similar to how ASP.NET tracks HttpContext.
    /// </summary>
    public static class StepContext
    {
        public static readonly AsyncLocal<string?> Current = new AsyncLocal<string?>();
    }
}
