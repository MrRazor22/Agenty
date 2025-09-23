using Agenty.AgentCore.Runtime;
using Agenty.AgentCore.Steps;
using Agenty.AgentCore.Steps.RAG;
using Agenty.RAG;
using Agenty.RAG.Stores;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Agenty.AgentCore.Flows
{
    /// <summary>
    /// RAG-enabled chatbot: KB search first, then reflective QA loop.
    /// </summary>
    public sealed class RagChatFlow : IAgentStep<object, object>
    {
        private readonly StepExecutor _pipeline;

        public RagChatFlow(IRagRetriever retriever, int maxRounds = 5, string finalPrompt = "Summarize clearly with sources if possible.")
        {
            _pipeline = new StepExecutor.Builder()
                // 1. Search KB
                .Add(new KbSearchStep())

                // 2. If KB is weak, fallback to web
                .Branch<IReadOnlyList<SearchResult>>(
                    results => results == null || !results.Any() || results.Max(r => r.Score) < 0.6,
                    onWeak => onWeak.Add(new WebFallbackStep())
                )

                // 3. Inject context
                .Add(new ContextBuildStep())

                // 4. Reflection loop
                .Loop(
                    body => body
                        .Add(new ResponseStep())      // model answers
                        .Add(new ReflectiveQAStep()), // summarize, evaluate, finalize/replan
                    maxRounds: maxRounds
                )

                // 5. Safety net
                .Add(new FinalizeStep(finalPrompt))

                .Build();
        }

        public Task<object?> RunAsync(IAgentContext ctx, object? input = null)
            => _pipeline.RunAsync(ctx, input);
    }
}
