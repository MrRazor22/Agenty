using Agenty.AgentCore.TokenHandling;
using Agenty.LLMCore;
using Agenty.LLMCore.ChatHandling;
using Agenty.LLMCore.Logging;
using Agenty.LLMCore.ToolHandling;
using Agenty.RAG;
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
        Task<string> ExecuteAsync(IAgentContext context, string goal);
    }

    public interface IAgentContext
    {
        ILLMClient LLM { get; }
        ILLMCoordinator Tools { get; }
        IRagCoordinator? RAG { get; }
        ILogger Logger { get; }
        Conversation Conversation { get; }
        string? SystemPrompt { get; }
        string? Backstory { get; }
        ITokenManager TokenManager { get; }   // << unified entrypoint
        int MaxContextTokens { get; }
    }

    internal sealed class AgentContext : IAgentContext
    {
        public ILLMClient LLM { get; set; } = null!;
        public ILLMCoordinator Tools { get; set; } = null!;
        public IRagCoordinator? RAG { get; set; }
        public ILogger Logger { get; set; } = new ConsoleLogger("Agent", LogLevel.Debug);
        public Conversation Conversation { get; } = new();
        public string? SystemPrompt { get; set; }
        public string? Backstory { get; set; }
        public ITokenManager TokenManager { get; set; } = null!;
        public int MaxContextTokens { get; set; } = 4000;
    }

    public sealed class Agent : IAgent
    {
        private readonly AgentContext _ctx = new();
        private IExecutor? _executor;

        public IAgentContext Context => _ctx;

        private Agent()
        {
            var tokenizer = new SharpTokenTokenizer("gpt-3.5-turbo");
            _ctx.TokenManager = new DefaultTokenManager(tokenizer);
        }

        public static Agent Create() => new();

        // === Fluent config ===
        public Agent WithLLM(string baseUrl, string apiKey, string model = "any_model")
        {
            var llm = new LLMCore.Providers.OpenAI.OpenAILLMClient();
            llm.Initialize(baseUrl, apiKey, model);

            var registry = new ToolRegistry();
            _ctx.LLM = llm;
            _ctx.Tools = new LLMCoordinator(llm, registry);
            return this;
        }

        public Agent WithLogger(ILogger logger) { _ctx.Logger = logger; return this; }

        public Agent WithTools<T>() { _ctx.Tools.Registry.RegisterAll<T>(); return this; }
        public Agent WithTools(params Delegate[] fns) { _ctx.Tools.Registry.Register(fns); return this; }
        public Agent WithTools<T>(T? instance = default) { _ctx.Tools.Registry.RegisterAll(instance); return this; }

        public Agent WithTokenizer(ITokenizer tokenizer)
        {
            _ctx.TokenManager = new DefaultTokenManager(tokenizer);
            return this;
        }

        public Agent WithRAG(IEmbeddingClient embeddings, IVectorStore store, ITokenizer? tokenizer = null)
        {
            var tok = _ctx.TokenManager ?? new DefaultTokenManager(new SharpTokenTokenizer("gpt-3.5-turbo"));
            _ctx.RAG = new RagCoordinator(embeddings, store, tok, _ctx.Logger);
            return this;
        }

        public Agent WithInMemoryRAG(string embeddingModel, string baseUrl, string apiKey, string tokenizerModel = "gpt-3.5-turbo")
        {
            var embeddings = new LLMCore.Providers.OpenAI.OpenAIEmbeddingClient(baseUrl, apiKey, embeddingModel);
            var store = new InMemoryVectorStore(logger: _ctx.Logger);
            var tok = _ctx.TokenManager ?? new DefaultTokenManager(new SharpTokenTokenizer("gpt-3.5-turbo"));

            _ctx.RAG = new RagCoordinator(embeddings, store, tok, _ctx.Logger);
            return this;
        }

        public Agent WithSystemPrompt(string prompt)
        {
            _ctx.SystemPrompt = prompt;
            _ctx.Conversation.Add(Role.System, prompt);
            return this;
        }

        public Agent WithBackstory(string backstory)
        {
            _ctx.Backstory = backstory;
            _ctx.Conversation.Add(Role.System, $"Backstory: {backstory}");
            return this;
        }

        public Agent WithExecutor<T>() where T : IExecutor, new() { _executor = new T(); return this; }
        public Agent WithExecutor(IExecutor executor) { _executor = executor; return this; }

        // === Run ===
        public async Task<string> ExecuteAsync(string goal)
        {
            if (_ctx.LLM == null)
                throw new InvalidOperationException("LLM not configured. Call WithLLM().");

            if (_executor == null)
                throw new InvalidOperationException("Executor not set. Call WithExecutor().");

            // Trim before executing
            _ctx.TokenManager.Trim(_ctx.Conversation, _ctx.MaxContextTokens);

            _ctx.Conversation.Add(Role.User, goal);

            var answer = await _executor.ExecuteAsync(_ctx, goal);

            _ctx.Conversation.Add(Role.Assistant, answer);

            var report = _ctx.TokenManager.Report(_ctx.Conversation, _ctx.MaxContextTokens);
            _ctx.Logger?.Log(LogLevel.Debug, "OverallTokenReport", report.ToString());

            return answer;
        }
    }
}
