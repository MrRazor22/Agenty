using Agenty.AgentCore.Runtime;
using Agenty.AgentCore.Steps;
using Agenty.AgentCore.Steps.RAG;
using Agenty.BuiltInTools;
using Agenty.LLMCore;
using Agenty.LLMCore.ChatHandling;
using Agenty.LLMCore.ToolHandling;
using Agenty.RAG;
using Agenty.RAG.Stores;
using Microsoft.Extensions.Logging;

namespace Agenty.AgentCore.Executors
{
    /// <summary>
    /// Executor that combines RAG with tool-calling in a reflective loop:
    /// KB Search → (Optional Web Fallback) → Context Build → ToolCalling → Summarization → Evaluation → Finalization/Replanning.
    /// </summary>
    public sealed class RagToolCallingExecutor : IExecutor
    {
        private readonly int _maxRounds;

        public RagToolCallingExecutor(int maxRounds = 30)
        {
            _maxRounds = maxRounds;
        }

        public Task<object?> Execute(IAgentContext ctx)
        {
            var retriever = ctx.Memory.LongTerm ?? throw new InvalidOperationException("No RAG retriever configured in agent context.");

            // Warn if no RAGTools are available
            if (!ctx.Tools.GetTools(typeof(RAGTools)).Any())
                ctx.Logger.LogWarning("RagToolCallingExecutor running without any RAGTools registered.");

            var pipeline = new StepExecutor.Builder()
                // 1. Retrieve from KB
                .Add(new KbSearchStep(retriever))

                // 2. If KB is weak, fall back to web
                .Branch<IReadOnlyList<SearchResult>>(
                    results => results == null || results.Count == 0 || results.Max(r => r.Score) < 0.6,
                    onWeak => onWeak.Add(new WebFallbackStep(retriever))
                )

                // 3. Inject retrieved context into chat
                .Add(new ContextBuildStep())

                // 4. Main tool-calling + refinement loop
                .Add(new LoopStep(
                    new StepExecutor.Builder()
                        .Add(new ToolCallingStep())
                        .Add(new SummarizationStep())
                        .Add(new EvaluationStep())
                        .Branch<Answer, string>(
                            ans => ans?.confidence_score is Verdict.yes or Verdict.partial,
                            onYes => onYes.Add(new FinalizeStep("Give a final user friendly answer with sources if possible.")),
                            onNo => onNo.Add(new ReplanningStep())
                        )
                        .Build(),
                    maxRounds: _maxRounds
                ))

                // 5. Safety net
                .Add(new FinalizeStep("Answer clearly using all reasoning so far."))
                .Build();

            return pipeline.Execute(ctx);
        }
    }
}
