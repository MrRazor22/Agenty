using Agenty.RAG;
using Agenty.LLMCore;
using Agenty.LLMCore.Logging;
using Agenty.LLMCore.Providers.OpenAI;
using Agenty.LLMCore.ToolHandling;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Agenty.AgentCore
{
    public sealed class RAGAgent
    {
        private IRagCoordinator _coord = null!;
        private ILLMClient _llm = null!;
        private ILogger _logger = null!;
        private Gate? _grader;
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
            _grader = new Gate(new ToolCoordinator(_llm, new ToolRegistry()), _logger);
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
        public async Task<RAGResult> ExecuteAsync(string question, int topK = 3, int maxRounds = 5)
        {
            // Step 1: Retrieve context
            var results = await _coord.Search(question, topK);

            var context = string.Join("\n\n", results.Select(r => $"[{r.Source}] {r.Text}"));
            var sessionChat = new Conversation();
            _logger.AttachTo(sessionChat);

            sessionChat.Add(Role.System, "You are a concise QA assistant. Use retrieved context if provided. " +
                                         "Answer in <=3 sentences. Always mention source(s).")
                       .Add(Role.User, context + "\n\nQuestion: " + question);

            string finalAnswer = "";

            // Step 2: Iterative refinement
            for (int round = 0; round < maxRounds; round++)
            {
                var response = await _llm.GetResponse(sessionChat);
                sessionChat.Add(Role.Assistant, response);

                if (_grader == null)
                {
                    finalAnswer = response;
                    break;
                }

                var verdict = await _grader.CheckAnswer(question, sessionChat.ToString(~ChatFilter.System));

                if (verdict.confidence_score is Verdict.yes or Verdict.partial)
                {
                    finalAnswer = response;

                    if (verdict.confidence_score == Verdict.partial)
                        sessionChat.Add(Role.User, verdict.explanation);

                    sessionChat.Add(Role.User, "Rewrite your answer clearly and naturally for the user, in plain language.");

                    _globalChat.Add(Role.User, question)
                               .Add(Role.Assistant, finalAnswer);
                    break;
                }

                if (!results.Any()) break; // fallback if KB weak
                sessionChat.Add(Role.User, verdict.explanation);
            }

            // Step 3: Fallback if no final
            if (string.IsNullOrEmpty(finalAnswer))
            {
                sessionChat.Add(Role.User, "Return your best final answer clearly and naturally for the user, in plain language.");
                finalAnswer = await _llm.GetResponse(sessionChat);

                _globalChat.Add(Role.User, question)
                           .Add(Role.Assistant, finalAnswer);
            }

            var sources = results
                .Select(r => (r.Source, r.Score))
                .ToList();

            return new RAGResult(finalAnswer, sources);
        }

        public record RAGResult(string Answer, List<(string Source, double Score)> Sources);
    }
}
