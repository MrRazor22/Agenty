using Agenty.AgentCore.Runtime;
using Agenty.LLMCore;
using Agenty.LLMCore.ChatHandling;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Agenty.AgentCore.Steps
{
    // === StepExecutor === 
    public sealed class StepExecutor : IExecutor
    {
        private readonly Func<IAgentContext, Task<object?>> _fn;
        private StepExecutor(Func<IAgentContext, Task<object?>> fn) => _fn = fn;

        public Task<object?> Execute(IAgentContext ctx)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            return _fn(ctx);
        }

        // --- Fluent Builder ---
        public sealed class Builder
        {
            private Func<IAgentContext, Task<object?>>? _pipeline;

            public Builder Add<TIn, TOut>(IAgentStep<TIn, TOut> step)
            {
                var prev = _pipeline;
                _pipeline = async ctx =>
                {
                    object? input = prev == null ? default : await prev(ctx);

                    if (input is not TIn typed && input != null)
                        throw new InvalidCastException(
                            $"Pipeline type mismatch in {step.GetType().Name}: " +
                            $"expected {typeof(TIn).Name}, got {input.GetType().Name}");

                    return await RunStep(step, (TIn?)input, ctx);
                };
                return this;
            }

            private static async Task<TOut?> RunStep<TIn, TOut>(
                IAgentStep<TIn, TOut> step,
                TIn? input,
                IAgentContext ctx)
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    ctx.Logger?.LogDebug("Running {Step} with input {InputType}", step.GetType().Name, typeof(TIn).Name);

                    var result = await step.RunAsync(ctx, input);

                    ctx.Logger?.LogDebug(
                        "Step {Step} completed in {Elapsed}ms -> {OutputType}",
                        step.GetType().Name, sw.ElapsedMilliseconds, typeof(TOut).Name);

                    return result;
                }
                catch (Exception ex)
                {
                    ctx.Logger?.LogError(ex, "Step {Step} failed", step.GetType().Name);
                    throw;
                }
            }

            public Builder Branch<T>(
                Func<T?, bool> predicate,
                Action<Builder> onTrue,
                Action<Builder>? onFalse = null)
            {
                return Branch<T, T>(predicate, onTrue, onFalse);
            }

            public Builder Branch<TIn, TOut>(
                Func<TIn?, bool> predicate,
                Action<Builder> onTrue,
                Action<Builder>? onFalse = null)
            {
                var trueBuilder = new Builder();
                onTrue(trueBuilder);

                StepExecutor? falseExec = null;
                if (onFalse != null)
                {
                    var falseBuilder = new Builder();
                    onFalse(falseBuilder);
                    falseExec = falseBuilder.Build();
                }

                var branchStep = new BranchStep<TIn, TOut>(
                    predicate,
                    trueBuilder.Build(),
                    falseExec
                );

                return Add(branchStep);
            }

            public StepExecutor Build()
                => new StepExecutor(_pipeline ?? throw new InvalidOperationException("Pipeline is empty"));
        }
    }
}
