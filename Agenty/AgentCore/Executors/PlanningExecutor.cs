using Agenty.AgentCore.Runtime;
using Agenty.AgentCore.Steps;

namespace Agenty.AgentCore.Executors
{
    /// <summary>
    /// Executor that runs a structured planning + refinement loop:
    /// Planning → Summarization → Evaluation → Finalization/Replanning.
    /// </summary>
    public sealed class PlanningExecutor : IExecutor
    {
        private readonly IExecutor _pipeline;

        public PlanningExecutor(int maxRounds = 5)
        {
            _pipeline = new StepExecutor.Builder()
                // 1. Generate an initial plan
                .Add(new PlanningStep())

                // 2. Refinement loop
                .Add(new LoopStep(
                    new StepExecutor.Builder()
                        .Add(new SummarizationStep())   // condense plan into clear answer
                        .Add(new EvaluationStep())      // check quality
                        .Branch<Answer, string>(
                            ans => ans?.confidence_score is Verdict.yes or Verdict.partial,
                            onYes => onYes.Add(new FinalizeStep()),   // good enough → finalize
                            onNo => onNo.Add(new ReplanningStep())    // not good → replan
                        )
                        .Build(),
                    maxRounds: maxRounds
                ))
                .Build();
        }

        public Task<object?> Execute(IAgentContext ctx) => _pipeline.Execute(ctx);
    }
}
