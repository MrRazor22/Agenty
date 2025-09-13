using Agenty.LLMCore;
using Agenty.LLMCore.Logging;
using Agenty.LLMCore.Providers.OpenAI;
using Agenty.LLMCore.ToolHandling;
using Agenty.RAG;
using Agenty.RAG.IO;
using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Agenty.AgentCore
{
    public sealed class RAGAgent
    {
        private IRagCoordinator _coord = null!;
        private ILLMClient _llm = null!;
        private ILogger _logger = null!;
        private Gate? _gate;
        private readonly Conversation _chat = new();
        private readonly Conversation _globalChat = new();

        private int _maxContextTokens = 10000;

        public static RAGAgent Create() => new RAGAgent();
        private RAGAgent() { }

        // === Configuration ===
        public RAGAgent WithLLM(string baseUrl, string apiKey, string model = "any_model")
        {
            _llm = new OpenAILLMClient();
            _llm.Initialize(baseUrl, apiKey, model);
            return this;
        }

        public RAGAgent WithLogger(ILogger logger)
        {
            _logger = logger;
            _logger.AttachTo(_chat);
            _gate = new Gate(new ToolCoordinator(_llm, new ToolRegistry()), _logger);
            return this;
        }

        public RAGAgent WithRAG(IEmbeddingClient embeddings, IVectorStore store, ITokenizer tokenizer, ILogger? logger = null)
        {
            _coord = new RagCoordinator(
                embeddings,
                store,
                tokenizer,
                logger ?? _logger
            );
            return this;
        }

        public IRagCoordinator Knowledge => _coord;

        // === Main Loop ===
        public async Task<string> ExecuteAsync(string goal, int topK = 3, int maxRounds = 5, double minScore = 0.6)
        {
            // 1. Search KB
            var kbResults = await _coord.Search(goal, topK, SearchScope.KnowledgeBase);
            var results = kbResults;

            // 2. If KB weak → fallback to web
            if (!kbResults.Any() || kbResults.Max(r => r.Score) < minScore)
            {
                var webDocs = await WebSearchLoader.SearchAsync(goal, topK);
                if (webDocs.Any())
                {
                    await _coord.AddDocumentsAsync(webDocs.Select(d => (d.Doc, d.Source)), persist: false);
                    var webResults = await _coord.Search(goal, topK, SearchScope.Session);

                    results = kbResults.Concat(webResults)
                                       .OrderByDescending(r => r.Score)
                                       .Take(topK)
                                       .ToList();
                }
            }

            // 3. Build context
            var context = results.Any()
                ? string.Join("\n\n", results.Select(r => $"[{r.Source}] {r.Text}"))
                : "";

            var sessionChat = new Conversation();
            _logger.AttachTo(sessionChat);
            sessionChat.Add(Role.System, "You are a friendly assistant. Use retrieved context if provided.")
                        .Add(Role.System, "Context:\n" + context)
                        .Append(_globalChat)
                        .Add(Role.User, goal);

            // 4. Generate + refine answer
            string answer = "";
            for (int round = 0; round < maxRounds; round++)
            {
                var resp = await _llm.GetResponse(sessionChat);
                sessionChat.Add(Role.Assistant, resp);

                if (_gate == null) { answer = resp; break; }

                var sum = await _gate!.SummarizeConversation(sessionChat, goal);
                var verdict = await _gate!.CheckAnswer(goal, sum.summariedAnswer);

                if (verdict.confidence_score is Verdict.yes or Verdict.partial)
                {
                    sessionChat.Add(Role.Assistant, sum.summariedAnswer);
                    if (verdict.confidence_score == Verdict.partial)
                        sessionChat.Add(Role.User, verdict.explanation);

                    var final = await _llm.GetResponse(
                        sessionChat.Add(Role.User, "Give a final user friendly answer."),
                        LLMMode.Creative);

                    _globalChat.Add(Role.User, goal)
                               .Add(Role.Assistant, final);

                    answer = final;
                    break;
                }

                if (round < maxRounds - 1)
                    sessionChat.Add(Role.User, verdict.explanation);
            }

            // 5. Fallback if no good answer
            if (string.IsNullOrEmpty(answer))
            {
                sessionChat.Add(Role.User, "Give your best final answer clearly in plain language.");
                answer = await _llm.GetResponse(sessionChat);
            }

            _globalChat.Add(Role.User, goal).Add(Role.Assistant, answer);
            return answer;
        }




    }
}
