using Agenty.AgentCore.Runtime;
using Agenty.AgentCore.Steps;
using Agenty.LLMCore;
using Agenty.LLMCore.ChatHandling;
using System.Threading.Tasks;

namespace Agenty.AgentCore.Flows
{
    /// <summary>
    /// Composite step: reflective QA loop.
    /// Summarization → Evaluation → (Replanning if weak) → Finalization.
    /// </summary>
    public sealed class ReflectionFlow : IAgentStep<object, object>
    {
        private readonly StepExecutor _pipeline;

        public ReflectionFlow(int maxRounds = 5)
        {
            _pipeline = new StepExecutor.Builder()
                .Loop(
                    body => body
                        .Add(new ResponseStep())
                        .Add(new SummarizationStep())
                        .Add(new EvaluationStep(injectFeedback: true))
                        .Branch<Answer, string>(
                            when: ans => ans?.confidence_score is Verdict.yes or Verdict.partial,
                            onYes => onYes.Add(new FinalizeStep())
                        ),
                    maxRounds: maxRounds
                )
                // safety net if loop exits without a confident answer
                .Add(new FinalizeStep("Wrap up with a concise, user-friendly answer"))
                .Build();
        }

        public Task<object?> RunAsync(IAgentContext ctx, object? input = null)
            => _pipeline.RunAsync(ctx, input);
    }
}
