using Agenty.AgentCore.Runtime;
using Agenty.AgentCore.TokenHandling;
using Agenty.LLMCore;
using Agenty.LLMCore.ChatHandling;
using Agenty.LLMCore.Logging;
using Agenty.LLMCore.Providers.OpenAI;
using Agenty.LLMCore.ToolHandling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
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
        CancellationToken CancellationToken { get; }
    }

    public sealed class AgentResponse
    {
        public string? Message { get; private set; }
        public object? Payload { get; private set; }
        public TimeSpan Duration { get; internal set; }
        public int TokensUsed { get; internal set; }

        internal void Set(string? message = null, object? payload = null)
        {
            Message = message;
            Payload = payload;
        }
    }

    internal sealed class AgentContext : IAgentContext
    {
        private readonly AgentResponse _reponse = new AgentResponse();

        public AgentContext(IServiceProvider services, CancellationToken cancellationToken = default)
        {
            Services = services;
            CancellationToken = cancellationToken;
            Chat = new Conversation();
            Items = new Dictionary<string, object?>();
        }

        public Conversation Chat { get; }
        public string? UserRequest { get; set; }
        public AgentResponse Response => _reponse;
        public IServiceProvider Services { get; }
        public IDictionary<string, object?> Items { get; }
        public CancellationToken CancellationToken { get; }
        public void SetResult(string? message, object? payload) => _reponse.Set(message, payload);
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
            Services.AddLogging(builder => builder.AddConsole());
        }

        public Agent Build()
        {
            var provider = Services.BuildServiceProvider(validateScopes: true);

            // LLM parts are optional unless pipeline needs them
            var hasLLM = provider.GetService<ILLMClient>() != null
                      && provider.GetService<ILLMCoordinator>() != null;
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
        public Agent Use<TStep>() where TStep : IAgentStep
        {
            return Use((ctx, next) =>
            {
                // create step via DI if possible (uses ctx.Services scope)
                var step = (TStep)ActivatorUtilities.CreateInstance(ctx.Services, typeof(TStep));
                return step.InvokeAsync(ctx, next);
            });
        }

        // user-supplied factory (explicit constructor)
        public Agent Use<TStep>(Func<TStep> factory) where TStep : IAgentStep
        {
            return Use((ctx, next) => factory().InvokeAsync(ctx, next));
        }


        // run
        public async Task<AgentResponse> ExecuteAsync(string goal, CancellationToken ct = default)
        {
            using var scope = _services.CreateScope();
            var ctx = new AgentContext(scope.ServiceProvider, ct) { UserRequest = goal };

            try
            {
                ctx.Chat.CloneFrom(_history);
                ctx.Chat.AddUser(goal);

                // attach logger once per run
                var logger = ctx.Services.GetService<ILogger<Agent>>();
                logger?.AttachTo(ctx.Chat, "Agent");

                // Trim before execution
                var ctxTrimmer = ctx.Services.GetService<IContextTrimmer>();
                var usable = (int)(_maxContextTokens * 0.6); // keep 40% for headroom
                ctxTrimmer?.Trim(ctx.Chat, usable);

                var sw = Stopwatch.StartNew();
                await _pipeline(ctx);
                sw.Stop();

                ctx.Response.Duration = sw.Elapsed;

                var tokenMgr = ctx.Services.GetService<ITokenManager>();
                ctx.Response.TokensUsed = tokenMgr?.GetTotals().Total ?? 0;

                if (!string.IsNullOrWhiteSpace(ctx.Response.Message))
                    _history.AddAssistant(ctx.Response.Message!);
            }
            catch (Exception ex)
            {
                ctx.Services.GetService<ILogger<Agent>>()?.LogError(ex, "Agent execution failed");
                ctx.SetResult($"Error: {ex.Message}", null);
            }

            return ctx.Response;
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

            builder.Services.AddSingleton<ILLMClient>(sp =>
            {
                var client = new OpenAILLMClient();
                client.Initialize(options.BaseUrl, options.ApiKey, options.Model);
                return client;
            });

            builder.Services.AddSingleton<ILLMCoordinator>(sp =>
            {
                var llmClient = sp.GetRequiredService<ILLMClient>();
                var registry = sp.GetRequiredService<IToolCatalog>();
                var runtime = sp.GetRequiredService<IToolRuntime>();
                var tokenManager = sp.GetRequiredService<ITokenManager>();
                var parser = new ToolCallParser();
                var retry = new DefaultRetryPolicy();
                return new LLMCoordinator(llmClient, registry, runtime, parser, tokenManager, retry);
            });

            return builder;
        }
    }
}
