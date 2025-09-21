using Agenty.AgentCore.TokenHandling;
using Agenty.LLMCore;
using Agenty.LLMCore.ChatHandling;
using Agenty.LLMCore.Logging;
using Agenty.LLMCore.ToolHandling;
using Agenty.RAG;
using Agenty.RAG.Embeddings;
using Agenty.RAG.Embeddings.Providers.OpenAI;
using Agenty.RAG.Stores;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace Agenty.AgentCore
{
    public interface IAgent
    {
        IAgentContext Context { get; }
        Task<string> ExecuteAsync(string goal);
    }

    public interface IExecutor
    {
        Task<object?> Execute(IAgentContext ctx);
    }

    /// <summary>
    /// Minimal contract for agent execution.
    /// </summary>
    public interface IAgentContext
    {
        IToolRegistry Tools { get; }
        ILLMOrchestrator LLM { get; }
        ILogger Logger { get; }
        IMemory Memory { get; }
        string? SystemPrompt { get; }
        string? Backstory { get; }
        ITokenManager TokenManager { get; }
    }

    /// <summary>
    /// Memory abstraction: working (conversation) + long-term (RAG).
    /// </summary>
    public interface IMemory
    {
        Conversation Working { get; }
        IRagRetriever? LongTerm { get; }
    }

    internal sealed class AgentContext : IAgentContext, IMemory
    {
        // Core
        public IToolRegistry Tools { get; } = new ToolRegistry();
        public ILLMOrchestrator LLM { get; set; } = null!;
        public ILogger Logger { get; set; } = new ConsoleLogger("Agent", LogLevel.Debug);
        public ITokenManager TokenManager { get; set; } = null!;

        // Memory
        public Conversation Working { get; } = new();
        public IRagRetriever? LongTerm { get; set; }

        // Metadata
        public string? SystemPrompt { get; set; }
        public string? Backstory { get; set; }

        // Expose memory wrapper
        public IMemory Memory => this;
    }

    public sealed class Agent : IAgent
    {
        private readonly AgentContext _ctx = new();
        private IExecutor? _executor;
        private int _maxTokenBudget = 4000;
        private ITokenizer _tokenizer;

        public IAgentContext Context => _ctx;

        private Agent()
        {
            _tokenizer = new SharpTokenTokenizer("gpt-3.5-turbo");
            _ctx.TokenManager = new SlidingWindowTokenManager(_tokenizer);
        }

        public static Agent Create() => new();

        // === Fluent config ===
        public Agent WithLLM(string baseUrl, string apiKey, string model = "any_model")
        {
            var llmClient = new LLMCore.Providers.OpenAI.OpenAILLMClient();
            llmClient.Initialize(baseUrl, apiKey, model);

            _ctx.LLM = new LLMOrchestrator(
                llmClient,
                _ctx.Tools,
                new ToolRuntime(_ctx.Tools),
                new ToolCallParser(),
                new DefaultRetryPolicy()
            );
            return this;
        }

        public Agent WithLogger(ILogger logger)
        {
            _ctx.Logger = logger;
            return this;
        }
        public Agent WithTools<T>(params string[] tags)
        {
            _ctx.Tools.RegisterAll<T>(tags);
            return this;
        }

        public Agent WithTools<T>(T instance, params string[] tags)
        {
            _ctx.Tools.RegisterAll(instance, tags);
            return this;
        }

        public Agent WithTokenizer(ITokenizer tokenizer)
        {
            _ctx.TokenManager = new SlidingWindowTokenManager(tokenizer, _maxTokenBudget);
            return this;
        }

        public Agent WithMaxContextTokens(int tokenBudget)
        {
            _ctx.TokenManager = new SlidingWindowTokenManager(_tokenizer, tokenBudget);
            return this;
        }

        public Agent WithTokenManager(ITokenManager tokenManager)
        {
            _ctx.TokenManager = tokenManager;
            return this;
        }

        public Agent WithRAG(IEmbeddingClient embeddings, IVectorStore store)
        {
            var tok = _ctx.TokenManager.Tokenizer;
            _ctx.LongTerm = new RagRetriever(embeddings, store, tok, _ctx.Logger);
            return this;
        }

        public Agent WithInMemoryRAG(string embeddingModel, string baseUrl, string apiKey)
        {
            var embeddings = new OpenAIEmbeddingClient(baseUrl, apiKey, embeddingModel);
            var store = new InMemoryVectorStore();
            var tok = _ctx.TokenManager.Tokenizer;
            _ctx.LongTerm = new RagRetriever(embeddings, store, tok, _ctx.Logger);
            return this;
        }

        public Agent WithSystemPrompt(string prompt)
        {
            _ctx.SystemPrompt = prompt;
            _ctx.Working.Add(Role.System, prompt);
            return this;
        }

        public Agent WithBackstory(string backstory)
        {
            _ctx.Backstory = backstory;
            _ctx.Working.Add(Role.System, $"Backstory: {backstory}");
            return this;
        }

        public Agent WithExecutor<T>() where T : IExecutor, new()
        {
            _executor = new T();
            return this;
        }

        public Agent WithExecutor(IExecutor executor)
        {
            _executor = executor;
            return this;
        }

        // === Run ===
        public async Task<string> ExecuteAsync(string goal)
        {
            if (_ctx.LLM == null)
                throw new InvalidOperationException("LLM not configured. Call WithLLM().");

            if (_executor == null)
                throw new InvalidOperationException("Executor not set. Call WithExecutor().");

            // Trim before executing
            _ctx.TokenManager.Trim(_ctx.Working);

            _ctx.Working.Add(Role.User, goal);

            var answer = await _executor.Execute(_ctx);

            if (answer is string text)
                _ctx.Working.Add(Role.Assistant, text);

            _ctx.Logger.LogUsage(_ctx.TokenManager.Report(_ctx.Working), "Overall Agent Token Usage");
            return answer?.ToString() ?? string.Empty;
        }
    }
}
