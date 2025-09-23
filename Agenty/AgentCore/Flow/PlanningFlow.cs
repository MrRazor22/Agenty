using Agenty.AgentCore.Runtime;
using Agenty.AgentCore.Steps;
using Agenty.AgentCore.Steps.Domain;

namespace Agenty.AgentCore.Flows
{
    /// <summary>
    /// Composite step: Planning → (Summarization → Evaluation → Finalize/Replan)*.
    /// </summary>
    public sealed class PlanningFlow : IAgentStep<object, object>
    {
        private readonly StepExecutor _pipeline;

        public PlanningFlow(int maxRounds = 5)
        {
            _pipeline = new StepExecutor.Builder()
                // 1. Generate an initial plan
                .Add(new PlanningStep())

                // 2. Refinement loop (flattened, no nested StepExecutor)
                .Loop(
                    body => body
                        .Add(new SummarizationStep())   // condense plan into clear answer
                        .Add(new EvaluationStep())      // check quality
                        .Branch<Answer, string>(
                            ans => ans?.confidence_score is Verdict.yes or Verdict.partial,
                            onYes => onYes.Add(new FinalizeStep()),   // good enough → finalize
                            onNo => onNo.Add(new ReplanningStep())   // not good → replan
                        ),
                    maxRounds: maxRounds
                )
                .Build();
        }

        public Task<object?> RunAsync(IAgentContext ctx, object? input = null)
            => _pipeline.RunAsync(ctx, input);
    }
}
