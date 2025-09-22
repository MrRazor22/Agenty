using Agenty.AgentCore.Runtime;
using Agenty.LLMCore;
using Agenty.LLMCore.ChatHandling;

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
        private readonly StepExecutor? _onFalse; // optional

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
            var branch = _predicate(input) ? _onTrue : _onFalse;

            if (branch == null)
            {
                if (input is TOut passthrough)
                    return passthrough;
                return default;
            }

            var result = await branch.Execute(ctx);

            if (result is TOut typed)
                return typed;

            throw new InvalidCastException(
                $"Branch returned {result?.GetType().Name ?? "null"}, expected {typeof(TOut).Name}");
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
            _breakCondition = breakCondition ?? (result => result is string && !string.IsNullOrWhiteSpace(result as string)); // default stop rule
            _maxRounds = maxRounds;
        }

        public async Task<object?> RunAsync(IAgentContext ctx, object? input = null)
        {
            object? lastResult = null;

            for (int round = 0; round < _maxRounds; round++)
            {
                lastResult = await _body.Execute(ctx);

                if (_breakCondition(lastResult))
                    return lastResult;
            }

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
