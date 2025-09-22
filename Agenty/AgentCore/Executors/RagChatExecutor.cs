using Agenty.AgentCore.Steps;
using Agenty.AgentCore.Steps.RAG;
using Agenty.RAG;
using Agenty.RAG.Stores;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Agenty.AgentCore.Executors
{
    /// <summary>
    /// RAG-enabled chatbot: KB search first, then reflective QA loop.
    /// </summary>
    public sealed class RagChatPipeline : IAgentStep<object, object>
    {
        private readonly StepExecutor _pipeline;

        public RagChatPipeline(IRagRetriever retriever, int maxRounds = 5, string finalPrompt = "Summarize clearly with sources if possible.")
        {
            _pipeline = new StepExecutor.Builder()
                .Add(new KbSearchStep())   // search KB
                .Branch<IReadOnlyList<SearchResult>>(
                    results => results == null || !results.Any() || results.Max(r => r.Score) < 0.6,
                    onWeak => onWeak.Add(new WebFallbackStep())
                )
                .Add(new ContextBuildStep())        // add context into chat
                .Add(new LoopStep(
                    new StepExecutor.Builder()
                        .Add(new ResponseStep())       // model answers
                        .Add(new ReflectiveQAStep())   // summarize, evaluate, finalize/replan
                        .Build(),
                    maxRounds: maxRounds
                ))
                .Add(new FinalizeStep(finalPrompt))   // safety net
                .Build();
        }

        public Task<object?> RunAsync(IAgentContext ctx, object? input = null)
            => _pipeline.RunAsync(ctx, input);
    }
}
