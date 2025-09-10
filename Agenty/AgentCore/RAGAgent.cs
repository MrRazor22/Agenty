using Agenty.RAG;
using Agenty.LLMCore;
using Agenty.LLMCore.Logging;
using Agenty.LLMCore.Providers.OpenAI;
using Agenty.LLMCore.ToolHandling;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Agenty.AgentCore
{
    public sealed class RAGAgent
    {
        private RagCoordinator _coord = null!;
        private ILLMClient _llm = null!;
        private ILogger _logger = null!;
        private Gate? _grader;
        private readonly Conversation _chat = new();
        private readonly Conversation _globalChat = new();

        private int _maxContextTokens = 10000;
        private string _tokenizerModel = "gpt-3.5-turbo";

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

        public RAGAgent WithRAG(
    IEmbeddingClient embeddings,
    IVectorStore store,
    string? savePath = null,
    ILogger? logger = null)
        {
            _coord = new RagCoordinator(
                embeddings,
                store,
                logger ?? _logger,
                autoSaveDir: savePath
            );
            return this;
        }

        public IRagCoordinator Knowledge => _coord;
        public async Task<RAGResult> ExecuteAsync(
            string question,
            int topK = 3,
            int maxRounds = 5)
        {
            // Step 1: Retrieve
            var retrieved = (await _coord.Search(question, topK)).ToList();
            if (!retrieved.Any())
                retrieved = (await _coord.Search(question, topK * 2)).ToList();

            // Step 2: Build context
            var contextChunks = retrieved.Select(r => $"[{r.source}] {r.chunk}");
            string context = RAGHelper.AssembleContext(contextChunks, _maxContextTokens, _tokenizerModel);

            var sessionChat = new Conversation();
            _logger.AttachTo(sessionChat);
            sessionChat.Add(Role.System, "You are a concise QA assistant. Use retrieved context if provided. " +
                                         "Answer in <=3 sentences. Always mention source(s).")
                       .Add(Role.User, context + "\n\nQuestion: " + question);

            string finalAnswer = "";
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
                    sessionChat.Add(Role.Assistant, response);

                    if (verdict.confidence_score == Verdict.partial)
                        sessionChat.Add(Role.User, verdict.explanation);

                    finalAnswer = await _llm.GetResponse(
                        sessionChat.Add(Role.User, "Give a final user friendly answer."),
                        LLMMode.Creative);

                    _globalChat.Add(Role.User, question)
                               .Add(Role.Assistant, finalAnswer);

                    break;
                }

                if (!retrieved.Any()) break; // fallback if KB weak
                sessionChat.Add(Role.User, verdict.explanation);
            }

            // Step 4: Fallback if still empty
            if (string.IsNullOrEmpty(finalAnswer))
            {
                sessionChat.Add(Role.User, "Max rounds reached. Return the best final answer now as plain text with sources.");
                finalAnswer = await _llm.GetResponse(sessionChat);
                _globalChat.Add(Role.User, question)
                           .Add(Role.Assistant, finalAnswer);
            }

            var sources = retrieved.Select(r => (r.source, r.score)).ToList();
            return new RAGResult(finalAnswer, sources);
        }

        public record RAGResult(string Answer, List<(string Source, double Score)> Sources);
    }
}
