using Agenty.Helper;
using Agenty.LLMCore;
using Agenty.LLMCore.Logging;
using Agenty.LLMCore.Providers.OpenAI;
using Agenty.LLMCore.ToolHandling;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Agenty.AgentCore
{
    public sealed class RAGAgent
    {
        private ILLMClient _llm = null!;
        private IEmbeddingClient _embeddings = null!;
        private ToolCoordinator _coord = null!;
        private readonly IToolRegistry _tools = new ToolRegistry();
        private readonly List<(string chunk, float[] vector)> _knowledgeBase = new();

        private readonly Conversation chat = new();
        private ILogger _logger = null!;
        private Gate? _grader;

        public static RAGAgent Create() => new();
        private RAGAgent() { }

        // === Configuration ===
        public RAGAgent WithLLM(string baseUrl, string apiKey, string model = "any_model")
        {
            _llm = new OpenAILLMClient();
            _llm.Initialize(baseUrl, apiKey, model);
            _coord = new ToolCoordinator(_llm, _tools);
            return this;
        }

        public RAGAgent WithEmbeddings(IEmbeddingClient embeddingClient)
        {
            _embeddings = embeddingClient;
            return this;
        }

        public RAGAgent WithTools<T>() { _tools.RegisterAll<T>(); return this; }
        public RAGAgent WithTools(params Delegate[] fns) { _tools.Register(fns); return this; }

        public RAGAgent WithLogger(ILogger logger)
        {
            _logger = logger;
            _grader = new Gate(_coord, _logger);
            _logger.AttachTo(chat);
            return this;
        }

        // === Knowledge Base ===
        public async Task<RAGAgent> AddDocumentsAsync(IEnumerable<string> docs)
        {
            EnsureEmbeddings();

            var vectors = await _embeddings.GetEmbeddingsAsync(docs);
            int i = 0;
            foreach (var doc in docs)
                _knowledgeBase.Add((doc, vectors[i++]));

            return this;
        }

        public async Task<RAGAgent> AddDocumentAsync(string doc)
        {
            EnsureEmbeddings();

            var vector = await _embeddings.GetEmbeddingAsync(doc);
            _knowledgeBase.Add((doc, vector));

            return this;
        }

        public async Task<float[]> EmbedAsync(string text)
        {
            EnsureEmbeddings();
            return await _embeddings.GetEmbeddingAsync(text);
        }

        // === Search ===
        public async Task<IEnumerable<(string chunk, double score)>> SearchAsync(string query, int topK = 3)
        {
            EnsureEmbeddings();

            var qVec = await _embeddings.GetEmbeddingAsync(query);

            return _knowledgeBase
                .Select(k => (k.chunk, score: VectorMath.CosineSimilarity(qVec, k.vector)))
                .OrderByDescending(x => x.score)
                .Take(topK)
                .ToList();
        }

        // === RAG Execution ===
        public async Task<string> ExecuteAsync(string goal, int maxRounds = 50, int topK = 3)
        {
            var contextChunks = (await SearchAsync(goal, topK)).Select(x => x.chunk).ToList();
            string context = contextChunks.Any()
                ? "Use the following context to answer:\n\n" + string.Join("\n\n", contextChunks)
                : "";

            chat.Add(Role.System, "You are a concise QA assistant. Use retrieved context if provided. Answer in <=3 sentences.")
                .Add(Role.User, context + "\n\nQuestion: " + goal);

            for (int round = 0; round < maxRounds; round++)
            {
                var response = await _llm.GetResponse(chat);
                chat.Add(Role.Assistant, response);

                var answerGrade = await _grader!.CheckAnswer(goal, chat.ToString(~ChatFilter.System));
                if (answerGrade.confidence_score == Verdict.yes) return response;

                chat.Add(Role.User, answerGrade.explanation);
            }

            chat.Add(Role.User, "Max rounds reached. Return the best final answer now as plain text.");
            return await _llm.GetResponse(chat);
        }

        // === Safety ===
        private void EnsureEmbeddings()
        {
            if (_embeddings == null)
                throw new InvalidOperationException("Embeddings client not configured. Call WithEmbeddings() first.");
        }
    }
}
