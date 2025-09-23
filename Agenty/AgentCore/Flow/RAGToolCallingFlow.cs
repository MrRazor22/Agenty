using Agenty.AgentCore.Runtime;
using Agenty.AgentCore.Steps;
using Agenty.AgentCore.Steps.ControlFlow;
using Agenty.AgentCore.Steps.RAG;
using Agenty.BuiltInTools;
using Agenty.LLMCore;
using Agenty.LLMCore.ChatHandling;
using Agenty.LLMCore.ToolHandling;
using Agenty.RAG;
using Agenty.RAG.Stores;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Agenty.AgentCore.Flows
{
    /// <summary>
    /// Composite step: 
    /// KB Search → (Optional Web Fallback) → Context Build → ToolCalling Loop → Finalization.
    /// </summary>
    public sealed class RagToolCallingFlow : IAgentStep<object, object>
    {
        private readonly StepExecutor _pipeline;

        public RagToolCallingFlow(int maxRounds = 30)
        {
            _pipeline = new StepExecutor.Builder()
                // 1. Retrieve from KB
                .Add(new KbSearchStep())

                // 2. If KB is weak, fall back to web
                .Branch<IReadOnlyList<SearchResult>>(
                    results => results == null || results.Count == 0 || results.Max(r => r.Score) < 0.6,
                    onWeak => onWeak.Add(new WebFallbackStep())
                )

                // 3. Inject retrieved context into chat
                .Add(new ContextBuildStep())

                // 4. Main tool-calling + refinement loop
                .Add(new LoopStep(
                    body => body
                        .Add(new ToolCallingStep())
                        .Add(new SummarizationStep())
                        .Add(new EvaluationStep())
                        .Branch<Answer, string>(
                            ans => ans?.confidence_score is Verdict.yes or Verdict.partial,
                            onYes => onYes.Add(new FinalizeStep("Give a final user friendly answer with sources if possible.")),
                            onNo => onNo.Add(new ReplanningStep())
                        ),
                    maxRounds: maxRounds
                ))

                // 5. Safety net
                .Add(new FinalizeStep("Answer clearly using all reasoning so far."))
                .Build();
        }

        public Task<object?> RunAsync(IAgentContext ctx, object? input = null)
        {
            // sanity check: KB + tools
            var retriever = ctx.Memory.KnowledgeBase
                ?? throw new InvalidOperationException("No RAG retriever configured in agent context.");

            if (!ctx.Tools.GetTools(typeof(RAGTools)).Any())
                ctx.Logger?.LogWarning("RagToolCallingPipeline running without any RAGTools registered.");

            return _pipeline.RunAsync(ctx, input);
        }
    }
}
