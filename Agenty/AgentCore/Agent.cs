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
        Conversation ScratchPad { get; }
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
            ScratchPad = new Conversation();
            Items = new Dictionary<string, object?>();
        }

        public Conversation ScratchPad { get; }
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
            Services.AddSingleton<ToolRegistryCatalog>();
            Services.AddSingleton<IToolRegistry>(sp => sp.GetRequiredService<ToolRegistryCatalog>());
            Services.AddSingleton<IToolCatalog>(sp => sp.GetRequiredService<ToolRegistryCatalog>());
            Services.AddSingleton<IToolRuntime, ToolRuntime>();
            Services.AddSingleton<IContextTrimmer>(sp =>
            {
                var tokenizer = sp.GetRequiredService<ITokenizer>();
                var opts = new ContextTrimOptions(); // default: 8000, margin 0.8
                return new SlidingWindowTrimmer(tokenizer, opts);
            });

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
        public Agent Build()
        {
            var provider = Services.BuildServiceProvider(validateScopes: true);
            var hasLLM = provider.GetService<ILLMClient>() != null;

            if (!hasLLM)
                Console.WriteLine("[Warning] No LLM client/coordinator registered. Tool calls or planning may fail.");

            return new Agent(provider);
        }
    }

    public interface IAgent
    {
        Task<AgentResponse> InvokeAsync(string goal, CancellationToken cancellationToken = default);
    }
    // === 3. Agent (like WebApplication) ===
    public sealed class Agent : IAgent
    {
        private readonly IServiceProvider _services;
        private readonly Conversation _memory = new Conversation();
        private readonly IConversationStore? _store;

        private AgentStepDelegate _pipeline = ctx => Task.CompletedTask;
        public IServiceProvider Services => _services;
        public Conversation ChatMemory => _memory;

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
            _memory.AddSystem(prompt);
            return this;
        }


        // === Load/save conversation history explicitly ===
        public async Task LoadHistoryAsync(string sessionId)
        {
            if (_store == null) return;
            var past = await _store.LoadAsync(sessionId);
            if (past != null)
                _memory.Clone(past);
        }

        public async Task SaveHistoryAsync(string sessionId)
        {
            if (_store == null) return;
            await _store.SaveAsync(sessionId, _memory);
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
        public async Task<AgentResponse> InvokeAsync(string goal, CancellationToken ct = default)
        {
            using (var scope = _services.CreateScope())
            {
                var ctx = new AgentContext(scope.ServiceProvider, ct) { UserRequest = goal };

                try
                {
                    //load history to current context
                    ctx.ScratchPad.Clone(_memory);
                    ctx.ScratchPad.AddUser(goal);

                    // Execute pipeline
                    await _pipeline(ctx);

                    // Update persistent history ONLY after success
                    _memory.AddUser(goal);
                    _memory.AddAssistant(ctx.Response.Message);
                }
                catch (Exception ex)
                {
                    // Failure = scratchpad discarded, memory unchanged
                    var logger = ctx.Services.GetService<ILogger<Agent>>();
                    logger?.LogError(ex, "Agent execution failed");
                    ctx.SetResult("Error: " + ex.Message, null);
                }

                return ctx.Response;
            }
        }

        public static AgentBuilder CreateBuilder() => new AgentBuilder();
    }
}
