using Agenty.AgentCore.Runtime;
using Microsoft.Extensions.Logging;

namespace Agenty.AgentCore.Steps.ControlFlow
{
    public sealed class ErrorStep : IAgentStep<StepFailure, object>
    {
        private readonly StepExecutor _errorPipeline;

        public ErrorStep(Action<StepExecutor.Builder> errorPipeline)  // FIX: accept builder lambda
        {
            var builder = new StepExecutor.Builder();
            errorPipeline?.Invoke(builder);
            _errorPipeline = builder.Build();
        }

        public async Task<object?> RunAsync(IAgentContext ctx, StepFailure? failure = null)
        {
            if (failure == null)
                return null;

            ctx.Logger?.LogWarning(
                "ErrorStep handling StepFailure from {Step}: {Message}",
                failure.Step, failure.Error?.Message
            );

            try
            {
                return await _errorPipeline.RunAsync(ctx, failure);
            }
            catch (Exception ex)
            {
                ctx.Logger?.LogError(ex, "ErrorStep pipeline itself failed");
                return $"Agent encountered an error in {failure.Step}, and recovery also failed.";
            }
        }
    }

}
