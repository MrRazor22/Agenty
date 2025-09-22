using Agenty.AgentCore.Steps;
using Agenty.LLMCore;
using Agenty.LLMCore.ChatHandling;

namespace Agenty.AgentCore.Executors
{
    /// <summary>
    /// Composite step: Planning → Loop(ToolCalling → ReflectiveQA) → Finalization.
    /// </summary>
    public sealed class ToolCallingPipeline : IAgentStep<object, object>
    {
        private readonly StepExecutor _pipeline;

        public ToolCallingPipeline(int maxRounds = 10, string finalPrompt = "Give a final user friendly answer with sources if possible.")
        {
            _pipeline = new StepExecutor.Builder()
                .Add(new PlanningStep())
                .Add(new LoopStep(
                    new StepExecutor.Builder()
                        .Add(new ToolCallingStep())
                        .Add(new ReflectiveQAStep())
                        .Build(),
                    maxRounds: maxRounds
                ))
                .Add(new FinalizeStep(finalPrompt))
                .OnError(
                    new StepExecutor.Builder()
                        .Add(new MapStep<StepFailure, string>(f => $"Sorry, {f.Step} failed: {f.Error?.Message}"))
                        .Add(new FinalizeStep("Return user-friendly error"))
                        .Build()
                )
                .Build();
        }

        public Task<object?> RunAsync(IAgentContext ctx, object? input = null)
            => _pipeline.RunAsync(ctx, input);
    }
}
