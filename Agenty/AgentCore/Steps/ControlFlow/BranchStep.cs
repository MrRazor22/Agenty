using Agenty.AgentCore.Runtime;
using Microsoft.Extensions.Logging;

namespace Agenty.AgentCore.Steps.ControlFlow
{
    // BranchStep: picks pipeline based on typed predicate (flat builder)
    public sealed class BranchStep<TIn, TOut> : IAgentStep<TIn, TOut>
    {
        private readonly Func<TIn?, bool> _predicate;
        private readonly StepExecutor _onTrue;
        private readonly StepExecutor? _onFalse;

        public BranchStep(
            Func<TIn?, bool> predicate,
            Action<StepExecutor.Builder> onTrue,
            Action<StepExecutor.Builder>? onFalse = null)
        {
            _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));

            var trueBuilder = new StepExecutor.Builder();
            onTrue?.Invoke(trueBuilder);
            _onTrue = trueBuilder.Build();

            if (onFalse != null)
            {
                var falseBuilder = new StepExecutor.Builder();
                onFalse(falseBuilder);
                _onFalse = falseBuilder.Build();
            }
        }

        public async Task<TOut?> RunAsync(IAgentContext ctx, TIn? input = default)
        {
            var takeTrue = _predicate(input);
            ctx.Logger?.LogDebug(
                "BranchStep<{In},{Out}> predicate={Predicate} => branch={Branch}",
                typeof(TIn).Name, typeof(TOut).Name,
                takeTrue, takeTrue ? "onTrue" : "onFalse");

            var branch = takeTrue ? _onTrue : _onFalse;
            if (branch == null)
            {
                ctx.Logger?.LogDebug("BranchStep: no branch pipeline. Passing through input.");
                if (input is TOut passthrough)
                    return passthrough;
                return default;
            }

            var result = await branch.RunAsync(ctx, input);
            if (result is TOut typed) return typed;

            throw new InvalidCastException(
                $"Branch returned {result?.GetType().Name ?? "null"}, expected {typeof(TOut).Name}");
        }
    }
}
