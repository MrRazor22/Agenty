using Agenty.AgentCore.Steps;
using Agenty.LLMCore;
using Agenty.LLMCore.ChatHandling;

namespace Agenty.AgentCore.Executors
{
    /// <summary>
    /// Executor that runs a reflective QA loop using steps:
    /// Summarization → Evaluation → (Replanning if weak) → Finalization.
    /// </summary>
    public sealed class ReflectionExecutor : IExecutor
    {
        private readonly IExecutor _pipeline;

        public ReflectionExecutor(int maxRounds)
        {
            _pipeline = new StepExecutor.Builder()
                .Add(new LoopStep(
                    new StepExecutor.Builder()
                        .Add(new ResponseStep())
                        .Add(new SummarizationStep())
                        .Add(new EvaluationStep(injectFeedback: true))
                        .Branch<Answer, string>(
                            ans => ans?.confidence_score is Verdict.yes or Verdict.partial,
                            onYes => onYes.Add(new FinalizeStep())
                        )
                        .Build(),
                    maxRounds: 5
                ))
                .Add(new FinalizeStep("Wrap up with a concise, user-friendly answer"))// fallback if loop ends without high confidence
                .Build();
        }

        public Task<object?> Execute(IAgentContext ctx) => _pipeline.Execute(ctx);
    }
}
