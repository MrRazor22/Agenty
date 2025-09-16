using Agenty.LLMCore;
using Agenty.LLMCore.ChatHandling;
using Agenty.LLMCore.Logging;
using Agenty.LLMCore.ToolHandling;
using Agenty.RAG;
using Agenty.RAG.IO;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Agenty.AgentCore.Executors
{
    /// <summary>
    /// Executor that runs RAG: searches knowledge base first,
    /// falls back to web if needed, then refines the answer.
    /// </summary>
    public sealed class RAGExecutor : IExecutor
    {
        public async Task<string> ExecuteAsync(IAgentContext context, string goal)
        {
            if (context.RAG == null)
                throw new InvalidOperationException("RAG not initialized in AgentContext.");

            var coord = context.RAG;

            // 1. Search KB
            var kbResults = await coord.Search(goal, topK: 3, SearchScope.KnowledgeBase);
            var results = kbResults;

            // 2. If KB weak → fallback to web
            if (!kbResults.Any() || kbResults.Max(r => r.Score) < 0.6)
            {
                var webDocs = await WebSearchLoader.SearchAsync(goal, 3);
                if (webDocs.Any())
                {
                    await coord.AddDocumentsAsync(webDocs.Select(d => (d.Doc, d.Source)), persist: false);
                    var webResults = await coord.Search(goal, 3, SearchScope.Session);

                    results = kbResults.Concat(webResults)
                                       .OrderByDescending(r => r.Score)
                                       .Take(3)
                                       .ToList();
                }
            }

            // 3. Build context
            var contextText = results.Any()
                ? string.Join("\n\n", results.Select(r => $"[{r.Source}] {r.Text}"))
                : "";

            var sessionChat = new Conversation();
            context.Logger.AttachTo(sessionChat);
            sessionChat.Append(context.Conversation)
                        .Add(Role.System, "You are a friendly assistant. Use retrieved context if provided.")
                        .Add(Role.System, "Context:\n" + contextText);

            // 4. Generate + refine answer
            var answerEvaluator = new AnswerEvaluator(new LLMCoordinator(context.LLM, context.Tools.Registry), context.Logger);
            string answer = "";

            const int maxRounds = 5;
            for (int round = 0; round < maxRounds; round++)
            {
                var resp = await context.LLM.GetResponse(sessionChat);
                sessionChat.Add(Role.Assistant, resp);

                var sum = await answerEvaluator.Summarize(sessionChat, goal);
                var verdict = await answerEvaluator.EvaluateAnswer(goal, sum.summariedAnswer);

                if (verdict.confidence_score is Verdict.yes or Verdict.partial)
                {
                    sessionChat.Add(Role.Assistant, sum.summariedAnswer);

                    if (verdict.confidence_score == Verdict.partial)
                        sessionChat.Add(Role.User, verdict.explanation);

                    var final = await context.LLM.GetResponse(
                        sessionChat.Add(Role.User, "Give a final user friendly answer."),
                        LLMMode.Creative);

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
                answer = await context.LLM.GetResponse(sessionChat);
            }

            return answer;
        }
    }
}
