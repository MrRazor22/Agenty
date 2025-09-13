using Agenty.LLMCore;
using Agenty.LLMCore.Logging;
using Agenty.LLMCore.ToolHandling;
using Agenty.RAG;
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
        IToolCoordinator Tools { get; }
        IRagCoordinator? RAG { get; }
        ILogger Logger { get; }
        Conversation GlobalChat { get; }
        string? SystemPrompt { get; }
        string? Backstory { get; }
    }

    internal sealed class AgentContext : IAgentContext
    {
        public ILLMClient LLM { get; set; } = null!;
        public IToolCoordinator Tools { get; set; } = null!;
        public IRagCoordinator? RAG { get; set; }
        public ILogger Logger { get; set; } = null!;
        public Conversation GlobalChat { get; set; } = new();
        public string? SystemPrompt { get; set; }
        public string? Backstory { get; set; }
    }

    public sealed class Agent : IAgent
    {
        private readonly AgentContext _ctx = new();
        private IExecutor _executor = null!;
        public IAgentContext Context => _ctx;

        private Agent() { }

        public static Agent Create() => new Agent();

        // === Fluent config ===
        public Agent WithLLM(string baseUrl, string apiKey, string model = "any_model")
        {
            var llm = new LLMCore.Providers.OpenAI.OpenAILLMClient();
            llm.Initialize(baseUrl, apiKey, model);

            var registry = new ToolRegistry();
            var coordinator = new ToolCoordinator(llm, registry);

            _ctx.LLM = llm;
            _ctx.Tools = coordinator;
            return this;
        }

        public Agent WithLogger(ILogger logger) { _ctx.Logger = logger; return this; }

        public Agent WithTools<T>() { _ctx.Tools.Registry.RegisterAll<T>(); return this; }

        public Agent WithTools(params Delegate[] fns) { _ctx.Tools.Registry.Register(fns); return this; }
        public Agent WithTools<T>(T? instance = default)
        {
            _ctx.Tools.Registry.RegisterAll(instance);
            return this;
        }

        public Agent WithRAG(IEmbeddingClient embeddings, IVectorStore store, ITokenizer tokenizer)
        {
            _ctx.RAG = new RagCoordinator(embeddings, store, tokenizer, _ctx.Logger);
            return this;
        }

        public Agent WithSystemPrompt(string prompt)
        {
            _ctx.SystemPrompt = prompt;
            _ctx.GlobalChat.Add(Role.System, prompt);
            return this;
        }

        public Agent WithBackstory(string backstory)
        {
            _ctx.Backstory = backstory;
            _ctx.GlobalChat.Add(Role.System, $"Backstory: {backstory}");
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
            if (_executor == null) throw new InvalidOperationException("Executor not set.");

            _ctx.GlobalChat.Add(Role.User, goal);

            var answer = await _executor.ExecuteAsync(_ctx, goal);

            _ctx.GlobalChat.Add(Role.Assistant, answer);

            return answer;
        }
    }
}
