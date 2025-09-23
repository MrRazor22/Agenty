using Agenty.AgentCore.Steps.ControlFlow;
using Agenty.LLMCore;
using Agenty.LLMCore.ChatHandling;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Agenty.AgentCore.Runtime
{
    public record StepFailure(string Step, string Expected, string? Actual, Exception Error);

    public delegate Task<object?> StepDelegate(IAgentContext ctx, object? input);

    public sealed class StepExecutor : IAgentStep<object, object>
    {
        private readonly StepDelegate _pipeline;

        private StepExecutor(StepDelegate pipeline) => _pipeline = pipeline;

        public Task<object?> RunAsync(IAgentContext ctx, object? input = null)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            return _pipeline(ctx, input);
        }

        // --- Fluent Builder ---
        public sealed class Builder
        {
            private readonly List<Func<StepDelegate, StepDelegate>> _components = new();
            private Func<StepDelegate, StepDelegate>? _errorWrapper;

            public StepExecutor Build()
            {
                StepDelegate terminal = (ctx, input) => Task.FromResult(input);
                var pipeline = _components
                    .Reverse<Func<StepDelegate, StepDelegate>>()
                    .Aggregate(terminal, (next, comp) => comp(next));

                if (_errorWrapper != null)
                    pipeline = _errorWrapper(pipeline);

                return new StepExecutor(pipeline);
            }

            public Builder Add<TIn, TOut>(IAgentStep<TIn, TOut> step)
            {
                _components.Add(next => async (ctx, input) =>
                {
                    if (input is StepFailure failure && typeof(TIn) != typeof(StepFailure))
                        return failure; // only short-circuit if step doesn't accept StepFailure

                    if (input is not TIn typed && input != null)
                    {
                        var ex = new InvalidCastException(
                            $"Pipeline type mismatch in {step.GetType().Name}: expected {typeof(TIn).Name}, got {input.GetType().Name}");
                        ctx.Logger?.LogError(ex, "Type mismatch in {Step}", step.GetType().Name);
                        return new StepFailure(step.GetType().Name, typeof(TIn).Name, input.GetType().Name, ex);
                    }

                    try
                    {
                        var result = await RunStep(step, (TIn?)input, ctx);
                        return await next(ctx, result);
                    }
                    catch (Exception ex)
                    {
                        ctx.Logger?.LogError(ex, "Step {Step} failed", step.GetType().Name);
                        return new StepFailure(step.GetType().Name, typeof(TOut).Name, input?.GetType().Name, ex);
                    }
                });
                return this;
            }

            public Builder Branch<T>(Func<T?, bool> predicate, Action<Builder> onTrue, Action<Builder>? onFalse = null)
    => Branch<T, T>(predicate, onTrue, onFalse);

            public Builder Branch<TIn, TOut>(
    Func<TIn?, bool> when,
    Action<Builder> onTrue,
    Action<Builder>? onFalse = null)
            {
                _components.Add(next => async (ctx, input) =>
                {
                    if (input is StepFailure sf) return sf;

                    TIn? typed = default;
                    if (input is not TIn && input != null)
                    {
                        var ex = new InvalidCastException(
                            $"Branch input type mismatch: expected {typeof(TIn).Name}, got {input.GetType().Name}");
                        ctx.Logger?.LogError(ex, "Branch type mismatch");
                        return new StepFailure("Branch", typeof(TIn).Name, input?.GetType().Name, ex);
                    }
                    else
                    {
                        typed = (TIn?)input;
                    }

                    var trueBuilder = new Builder();
                    onTrue(trueBuilder);
                    var truePipeline = trueBuilder.Build()._pipeline;

                    StepDelegate? falsePipeline = null;
                    if (onFalse != null)
                    {
                        var fb = new Builder();
                        onFalse(fb);
                        falsePipeline = fb.Build()._pipeline;
                    }

                    bool takeTrue;
                    try { takeTrue = when(typed); }
                    catch (Exception ex)
                    {
                        ctx.Logger?.LogError(ex, "Branch predicate threw");
                        return new StepFailure("BranchPredicate", typeof(TIn).Name, input?.GetType().Name, ex);
                    }

                    var branchResult = takeTrue
                        ? await truePipeline(ctx, typed)
                        : falsePipeline != null ? await falsePipeline(ctx, typed) : input;

                    return await next(ctx, branchResult);
                });

                return this;
            }


            public Builder Loop(
                Action<Builder> configureBody,
                Func<object?, bool>? breakCondition = null,
                int maxRounds = 10)
            {
                _components.Add(next => async (ctx, input) =>
                {
                    if (input is StepFailure sf) return sf;

                    var bodyBuilder = new Builder();
                    configureBody(bodyBuilder);
                    var bodyPipeline = bodyBuilder.Build()._pipeline;

                    object? lastResult = null;
                    var bc = breakCondition ?? (r => r is string s && !string.IsNullOrWhiteSpace(s));

                    for (int i = 0; i < maxRounds; i++)
                    {
                        lastResult = await bodyPipeline(ctx, input);
                        if (lastResult is StepFailure sf2) return sf2;
                        if (bc(lastResult)) return await next(ctx, lastResult);
                    }

                    ctx.Logger?.LogWarning("Loop max rounds reached");
                    return await next(ctx, lastResult);
                });

                return this;
            }

            public Builder OnError(Builder errorBuilder)
            {
                if (errorBuilder == null) throw new ArgumentNullException(nameof(errorBuilder));
                return OnError(errorBuilder.Build());
            }

            public Builder OnError(StepExecutor errorPipeline)
            {
                if (errorPipeline == null) throw new ArgumentNullException(nameof(errorPipeline));
                var errorDelegate = errorPipeline._pipeline;

                _errorWrapper = inner => async (ctx, input) =>
                {
                    try
                    {
                        var result = await inner(ctx, input);
                        if (result is StepFailure sf)
                        {
                            ctx.Logger?.LogWarning("Delegating StepFailure from {Step} to error pipeline", sf.Step);
                            return await errorDelegate(ctx, sf); // ✅ run error pipeline
                        }
                        return result;
                    }
                    catch (Exception ex)
                    {
                        ctx.Logger?.LogError(ex, "Pipeline threw — delegating to error handler");
                        return await errorDelegate(ctx,
                            new StepFailure("Pipeline", "object", input?.GetType().Name, ex));
                    }
                };

                return this;
            }

            private static async Task<TOut?> RunStep<TIn, TOut>(
                IAgentStep<TIn, TOut> step, TIn? input, IAgentContext ctx)
            {
                var sw = Stopwatch.StartNew();
                var stepName = step.GetType().Name;

                ctx.Logger?.LogDebug("Running {Step} with input {InputType}", stepName, typeof(TIn).Name);
                if (ctx.Logger?.IsEnabled(LogLevel.Trace) == true)
                    ctx.Logger.LogTrace(">>> Input to {Step}: {Input}", stepName, input);

                var result = await step.RunAsync(ctx, input);

                ctx.Logger?.LogDebug("Step {Step} completed in {Elapsed}ms -> {OutputType}",
                    stepName, sw.ElapsedMilliseconds, typeof(TOut).Name);

                if (ctx.Logger?.IsEnabled(LogLevel.Trace) == true)
                    ctx.Logger.LogTrace("<<< Output from {Step}: {Output}", stepName, result);

                return result;
            }
        }
    }
}
