using Agenty.AgentCore.Runtime;
using Agenty.LLMCore;
using Agenty.LLMCore.ChatHandling;
using Microsoft.Extensions.Logging;

namespace Agenty.AgentCore.Steps
{
    // === Generic step interface (context-first) ===
    public interface IAgentStep<TIn, TOut>
    {
        Task<TOut?> RunAsync(IAgentContext ctx, TIn? input = default);
    }

    // BranchStep: picks pipeline based on typed predicate
    public sealed class BranchStep<TIn, TOut> : IAgentStep<TIn, TOut>
    {
        private readonly Func<TIn?, bool> _predicate;
        private readonly StepExecutor _onTrue;
        private readonly StepExecutor? _onFalse;

        public BranchStep(
            Func<TIn?, bool> predicate,
            StepExecutor onTrue,
            StepExecutor? onFalse = null)
        {
            _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
            _onTrue = onTrue ?? throw new ArgumentNullException(nameof(onTrue));
            _onFalse = onFalse;
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

    public sealed class ErrorStep : IAgentStep<StepFailure, object>
    {
        private readonly StepExecutor _errorPipeline;

        public ErrorStep(StepExecutor errorPipeline)
        {
            _errorPipeline = errorPipeline ?? throw new ArgumentNullException(nameof(errorPipeline));
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
                // Feed StepFailure as input to error pipeline
                return await _errorPipeline.RunAsync(ctx, failure);
            }
            catch (Exception ex)
            {
                ctx.Logger?.LogError(ex, "ErrorStep pipeline itself failed");
                return $"Agent encountered an error in {failure.Step}, and recovery also failed.";
            }
        }
    }


    public sealed class LoopStep : IAgentStep<object, object>
    {
        private readonly StepExecutor _body;
        private readonly Func<object?, bool> _breakCondition;
        private readonly int _maxRounds;

        public LoopStep(
            StepExecutor body,
            Func<object?, bool>? breakCondition = null,
            int maxRounds = 10)
        {
            _body = body ?? throw new ArgumentNullException(nameof(body));
            _breakCondition = breakCondition ?? (r => r is string s && !string.IsNullOrWhiteSpace(s));
            _maxRounds = maxRounds;
        }

        public async Task<object?> RunAsync(IAgentContext ctx, object? input = null)
        {
            object? lastResult = null;

            for (int round = 0; round < _maxRounds; round++)
            {
                ctx.Logger?.LogDebug("LoopStep iteration {Round}/{MaxRounds} started", round + 1, _maxRounds);

                lastResult = await _body.RunAsync(ctx, input);

                if (_breakCondition(lastResult))
                {
                    ctx.Logger?.LogDebug("LoopStep break condition met at round {Round}", round + 1);
                    return lastResult;
                }
            }

            ctx.Logger?.LogWarning("LoopStep max rounds ({MaxRounds}) reached. Returning last result.", _maxRounds);
            ctx.Memory.Working.Add(Role.System, $"[LoopStep] Max rounds ({_maxRounds}) reached, returning last result.");
            return lastResult;
        }
    }

    /// <summary>
    /// MapStep transforms one step’s output type into another,
    /// like Select/Map in LINQ.
    /// </summary>
    public sealed class MapStep<TIn, TOut> : IAgentStep<TIn, TOut>
    {
        private readonly Func<TIn?, TOut?> _mapper;

        public MapStep(Func<TIn?, TOut?> mapper)
        {
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        }

        public Task<TOut?> RunAsync(IAgentContext ctx, TIn? input = default)
        {
            var result = _mapper(input);
            return Task.FromResult(result);
        }
    }
}
