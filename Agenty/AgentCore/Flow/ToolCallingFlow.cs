using Agenty.AgentCore.Runtime;
using Agenty.AgentCore.Steps;
using Agenty.AgentCore.Steps.ControlFlow;
using Agenty.AgentCore.Steps.Domain;
using Agenty.LLMCore;
using Agenty.LLMCore.ChatHandling;

namespace Agenty.AgentCore.Flows
{
    /// <summary>
    /// Composite step: Planning → Loop(ToolCalling → ReflectiveQA) → Finalization.
    /// </summary>
    public sealed class ToolCallingFlow : IAgentStep<object, object>
    {
        private readonly StepExecutor _pipeline;

        public ToolCallingFlow(
      int maxRounds = 10,
      string finalPrompt = "Give a final user friendly answer with sources if possible.")
        {
            _pipeline = new StepExecutor.Builder()
                .Add(new PlanningStep())
                .Loop(
                    body => body
                        .Add(new ToolCallingStep())
                        .Add(new ReflectiveQAStep()),
                    maxRounds: maxRounds
                )
                .Add(new FinalizeStep(finalPrompt))
                .OnError(
                    new StepExecutor.Builder()
                        .Add(new MapStep<StepFailure, string>(
                            f => $"Sorry, {f.Step} failed: {f.Error?.Message}"
                        ))
                        .Add(new FinalizeStep("Return user-friendly error"))
                )
                .Build();
        }

        public Task<object?> RunAsync(IAgentContext ctx, object? input = null)
            => _pipeline.RunAsync(ctx, input);
    }
}
