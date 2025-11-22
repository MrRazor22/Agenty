using Agenty.AgentCore.TokenHandling;
using Agenty.LLMCore.ChatHandling;
using Agenty.LLMCore.Runtime;
using Agenty.LLMCore.ToolHandling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Agenty.AgentCore.Runtime
{
    // === 1. Context (like HttpContext) ===
    public interface IAgentContext
    {
        Conversation ScratchPad { get; }
        string? UserRequest { get; set; }
        AgentResponse Response { get; }
        IServiceProvider Services { get; }
        IDictionary<string, object?> Items { get; }
        Action<object>? Stream { get; set; }
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

    public sealed class AgentContext : IAgentContext
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
        public Action<object>? Stream { get; set; }
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
            Services.AddSingleton<IAgentExecutor, ToolCallingLoop>();
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
        Task<AgentResponse> InvokeAsync(string goal, CancellationToken cancellationToken = default, Action<object>? stream = null);
    }
    // === 3. Agent (like WebApplication) ===
    public class Agent : IAgent
    {
        private readonly IServiceProvider _services;
        private readonly Conversation _memory = new Conversation();
        private readonly IConversationStore? _store;
        private Func<IAgentExecutor>? _executorFactory;
        public IServiceProvider Services => _services;
        public Conversation ChatMemory => _memory;

        internal Agent(IServiceProvider services)
        {
            _services = services;
            _store = _services.GetService<IConversationStore>();
            _executorFactory = () => _services.GetRequiredService<IAgentExecutor>();
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

        public Agent UseExecutor(Func<IAgentExecutor> factory)
        {
            _executorFactory = factory ?? throw new ArgumentException(nameof(factory));
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

        // run
        public virtual async Task<AgentResponse> InvokeAsync(string goal, CancellationToken ct = default, Action<object>? stream = null)
        {
            bool responseSet = false;
            using (var scope = _services.CreateScope())
            {
                var ctx = new AgentContext(scope.ServiceProvider, ct) { UserRequest = goal };

                try
                {
                    //load history to current context
                    ctx.ScratchPad.Clone(_memory);
                    ctx.Stream = stream;

                    // Execute pipeline
                    var executor = _executorFactory!();
                    await executor.ExecuteAsync(ctx);

                    // Update persistent history ONLY after success
                    _memory.AddUser(goal);
                    _memory.AddAssistant(ctx.Response.Message);
                    responseSet = true;
                }
                catch (Exception ex)
                {
                    // Failure = scratchpad discarded, memory unchanged
                    var logger = ctx.Services.GetService<ILogger<Agent>>();
                    logger?.LogError(ex, "Agent execution failed");
                    ctx.SetResult("Error: " + ex.Message, null);
                }
                finally
                {
                    if (ctx.CancellationToken.IsCancellationRequested)
                    {
                        ctx.ScratchPad.AddAssistant("Operation canceled by user");
                        if (!responseSet)
                        {
                            ctx.SetResult("Error: Operation canceled by user.", null);
                        }
                    }
                    else if (!responseSet)
                    {
                        // Ensure some response is set
                        ctx.SetResult("Error: Agent failed to produce a response.", null);
                    }
                }
                return ctx.Response;
            }
        }

        public static AgentBuilder CreateBuilder() => new AgentBuilder();
    }
}
