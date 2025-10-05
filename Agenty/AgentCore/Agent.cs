using Agenty.AgentCore.Runtime;
using Agenty.AgentCore.TokenHandling;
using Agenty.LLMCore;
using Agenty.LLMCore.ChatHandling;
using Agenty.LLMCore.Logging;
using Agenty.LLMCore.ToolHandling;
using Microsoft.Extensions.Logging;
using RAGSharp.Embeddings;
using RAGSharp.Embeddings.Providers;
using RAGSharp.RAG;
using RAGSharp.Stores;
using System;
using System.Threading.Tasks;
using ITokenizer = Agenty.AgentCore.TokenHandling.ITokenizer;

namespace Agenty.AgentCore
{
    public interface IAgent
    {
        IAgentContext Context { get; }
        Task<string> ExecuteAsync(string goal);
    }
    /// <summary>
    /// Minimal contract for agent execution.
    /// </summary>
    public interface IAgentContext
    {
        IToolRegistry Tools { get; }
        ILLMCoordinator LLM { get; }
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
        IRagRetriever? KnowledgeBase { get; }
        IRagRetriever? SessionStore { get; }
    }

    internal sealed class AgentContext : IAgentContext, IMemory
    {
        // Core
        public IToolRegistry Tools { get; } = new ToolRegistry();
        public ILLMCoordinator LLM { get; set; } = null!;
        public ILogger Logger { get; set; } = new ConsoleLogger("Agent", LogLevel.Debug);
        public ITokenManager TokenManager { get; set; } = null!;

        // Memory
        public Conversation Working { get; } = new Conversation();
        public IRagRetriever? KnowledgeBase { get; set; }
        public IRagRetriever? SessionStore { get; set; }

        // Metadata
        public string? SystemPrompt { get; set; }
        public string? Backstory { get; set; }

        // Expose memory wrapper
        public IMemory Memory => this;
    }

    public sealed class Agent : IAgent
    {
        private readonly AgentContext _ctx = new AgentContext();
        private IAgentStep? _rootPipeline;
        private int _maxTokenBudget = 4000;
        private ITokenizer _tokenizer;


        public IAgentContext Context => _ctx;

        private Agent()
        {
            _tokenizer = new SharpTokenTokenizer("gpt-3.5-turbo");
            _ctx.TokenManager = new SlidingWindowTokenManager(_tokenizer);
            // default ephemeral/session store
            _ctx.SessionStore = new RagRetriever(
                new OpenAIEmbeddingClient("http://127.0.0.1:1234/v1", "lmstudio", "publisherme/bge/bge-large-en-v1.5-q4_k_m.gguf"),
                new InMemoryVectorStore()
            );
        }

        public static Agent Create() => new Agent();

        // === Fluent config ===
        public Agent WithLLM(string baseUrl, string apiKey, string model = "any_model")
        {
            var llmClient = new LLMCore.Providers.OpenAI.OpenAILLMClient();
            llmClient.Initialize(baseUrl, apiKey, model);

            _ctx.LLM = new LLMCoordinator(
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
            _ctx.Tools.RegisterAll<T>(instance, tags);
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
            _ctx.KnowledgeBase = new RagRetriever(embeddings, store);
            return this;
        }

        public Agent WithRAGRetriever(IRagRetriever ragRetriever)
        {
            var tok = _ctx.TokenManager.Tokenizer;
            _ctx.KnowledgeBase = ragRetriever;
            return this;
        }

        public Agent WithSessionRAG(string embeddingModel, string baseUrl, string apiKey)
        {
            var embeddings = new OpenAIEmbeddingClient(baseUrl, apiKey, embeddingModel);
            var store = new InMemoryVectorStore();
            var tok = _ctx.TokenManager.Tokenizer;

            _ctx.SessionStore = new RagRetriever(embeddings, store);
            return this;
        }

        public Agent WithKnowledgeBaseRAG(string embeddingModel, string baseUrl, string apiKey)
        {
            var embeddings = new OpenAIEmbeddingClient(baseUrl, apiKey, embeddingModel);
            var store = new FileVectorStore();
            var tok = _ctx.TokenManager.Tokenizer;
            _ctx.KnowledgeBase = new RagRetriever(embeddings, store);
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

        public Agent WithFlow(IAgentStep pipeline)
        {
            _rootPipeline = pipeline;
            return this;
        }

        // === Run ===
        public async Task<string> ExecuteAsync(string goal)
        {
            if (_ctx.LLM == null)
                throw new InvalidOperationException("LLM not configured. Call WithLLM().");

            if (_rootPipeline == null)
                throw new InvalidOperationException("Pipeline not set. Call WithPipeline().");

            _ctx.Logger.AttachTo(_ctx.Working);
            _ctx.TokenManager.Trim(_ctx.Working);

            _ctx.Working.Add(Role.User, goal);

            var answer = await _rootPipeline.RunAsync(_ctx);

            if (answer is string text)
                _ctx.Working.Add(Role.Assistant, text);

            _ctx.Logger.LogUsage(_ctx.TokenManager.Report(_ctx.Working), "Overall Agent Token Usage");
            return answer?.ToString() ?? string.Empty;
        }
    }
}
