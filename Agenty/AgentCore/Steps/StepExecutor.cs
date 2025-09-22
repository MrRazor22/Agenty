using Agenty.AgentCore.Runtime;
using Agenty.LLMCore;
using Agenty.LLMCore.ChatHandling;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Agenty.AgentCore.Steps
{
    public record StepFailure(
    string Step,
    string Expected,
    string? Actual,
    Exception Error);
    // === StepExecutor === 
    public sealed class StepExecutor : IAgentStep<object, object>
    {
        private readonly Func<IAgentContext, object?, Task<object?>> _fn;

        private StepExecutor(Func<IAgentContext, object?, Task<object?>> fn)
            => _fn = fn;

        public Task<object?> RunAsync(IAgentContext ctx, object? input = null)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            return _fn(ctx, input);
        }

        // --- Fluent Builder ---
        public sealed class Builder
        {
            private Func<IAgentContext, object?, Task<object?>>? _pipeline;
            private StepExecutor? _errorPipeline;

            public Builder Add<TIn, TOut>(IAgentStep<TIn, TOut> step)
            {
                var prev = _pipeline;
                _pipeline = async (ctx, inputObj) =>
                {
                    object? input = prev == null ? inputObj : await prev(ctx, inputObj);

                    if (input is StepFailure failure)
                    {
                        if (_errorPipeline != null)
                            return await _errorPipeline.RunAsync(ctx, failure);
                        return failure; // no handler → bubble
                    }

                    try
                    {
                        if (input is not TIn typed && input != null)
                        {
                            var ex = new InvalidCastException(
                                $"Pipeline type mismatch in {step.GetType().Name}: " +
                                $"expected {typeof(TIn).Name}, got {input.GetType().Name}");

                            ctx.Logger?.LogError(ex,
                                "Type mismatch in {Step}: expected {Expected}, got {Actual}",
                                step.GetType().Name, typeof(TIn).Name, input.GetType().Name);

                            // Instead of crashing pipeline, wrap as StepFailure
                            return new StepFailure(
                                step.GetType().Name,
                                typeof(TIn).Name,
                                input.GetType().Name,
                                ex
                            );
                        }

                        return await RunStep(step, (TIn?)input, ctx);
                    }
                    catch (Exception ex)
                    {
                        ctx.Logger?.LogError(ex, "Step {Step} failed", step.GetType().Name);

                        return new StepFailure(
                            Step: step.GetType().Name,
                            Expected: typeof(TOut).Name,
                            Actual: input?.GetType().Name,
                            Error: ex
                        );
                    }
                };
                return this;
            }

            private static async Task<TOut?> RunStep<TIn, TOut>(
                IAgentStep<TIn, TOut> step,
                TIn? input,
                IAgentContext ctx)
            {
                var sw = Stopwatch.StartNew();
                var stepName = step.GetType().Name;

                ctx.Logger?.LogDebug("Running {Step} with input {InputType}", stepName, typeof(TIn).Name);

                if (ctx.Logger?.IsEnabled(LogLevel.Trace) == true)
                {
                    ctx.Logger.LogTrace(">>> Input to {Step}: {Input}", stepName, input);
                }

                var result = await step.RunAsync(ctx, input);

                ctx.Logger?.LogDebug(
                      "Step {Step} completed in {Elapsed}ms -> {OutputType}",
                      stepName, sw.ElapsedMilliseconds, typeof(TOut).Name);

                if (ctx.Logger?.IsEnabled(LogLevel.Trace) == true)
                {
                    ctx.Logger.LogTrace("<<< Output from {Step}: {Output}", stepName, result);
                }

                return result;
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

            public Builder OnError(StepExecutor errorPipeline)
            {
                _errorPipeline = errorPipeline;
                return this;
            }

            public StepExecutor Build()
                => new StepExecutor(_pipeline ?? throw new InvalidOperationException("Pipeline is empty"));
        }
    }
}
