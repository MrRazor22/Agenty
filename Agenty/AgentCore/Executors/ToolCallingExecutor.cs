using Agenty.AgentCore.Steps;
using Agenty.LLMCore;
using Agenty.LLMCore.ChatHandling;

namespace Agenty.AgentCore.Executors
{
    /// <summary>
    /// Executor that lets the LLM call tools in a reflective loop:
    /// Planning → ToolCalling → Summarization → Evaluation → Finalization/Replanning.
    /// </summary>
    public sealed class ToolCallingExecutor : IExecutor
    {
        private readonly IExecutor _pipeline;

        public ToolCallingExecutor(int maxRounds = 10, string finalPrompt = "Give a final user friendly answer with sources if possible.")
        {
            _pipeline = new StepExecutor.Builder()
                .Add(new PlanningStep()) // optional one-time planning
                .Add(new LoopStep(
                    new StepExecutor.Builder()
                        .Add(new ToolCallingStep())
                        .Add(new ReflectiveQAStep())   // summarize, evaluate, finalize/replan
                        .Build(),
                    maxRounds: maxRounds
                ))
                .Add(new FinalizeStep(finalPrompt)) // safety net if loop exits without high confidence
                .Build();
        }

        public Task<object?> Execute(IAgentContext ctx) => _pipeline.Execute(ctx);
    }
}
