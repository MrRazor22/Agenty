using Agenty.AgentCore.Runtime;
using Agenty.LLMCore.ChatHandling;

namespace Agenty.AgentCore.Steps
{
    // === Generic step interfaces ===
    public interface IAgentStep<TIn, TOut>
    {
        Task<TOut?> RunAsync(
            Conversation chat,
            ILLMCoordinator llm,
            TIn? input = default);
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

        public async Task<TOut?> RunAsync(
            Conversation chat, ILLMCoordinator llm, TIn? input = default)
        {
            var branch = _predicate(input) ? _onTrue : _onFalse;

            if (branch == null)
            {
                if (input is TOut passthrough)
                    return passthrough;
                return default;
            }

            var result = await branch.Execute(chat, llm);

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

        public LoopStep(StepExecutor body, Func<object?, bool>? breakCondition = null)
        {
            _body = body ?? throw new ArgumentNullException(nameof(body));
            _breakCondition = breakCondition ?? (result => result is string); // default stop rule
        }

        public async Task<object?> RunAsync(
            Conversation chat, ILLMCoordinator llm, object? input = null)
        {
            while (true)
            {
                var result = await _body.Execute(chat, llm);

                if (_breakCondition(result))
                    return result;

                // otherwise, loop again
            }
        }
    }

    /// <summary>
    /// MapStep transforms one step’s output type into another, 
    /// Think of it like Select/Map in LINQ.
    /// </summary>
    public sealed class MapStep<TIn, TOut> : IAgentStep<TIn, TOut>
    {
        private readonly Func<TIn?, TOut?> _mapper;

        public MapStep(Func<TIn?, TOut?> mapper)
        {
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        }

        public Task<TOut?> RunAsync(
            Conversation chat,
            ILLMCoordinator llm,
            TIn? input = default)
        {
            // just map and return — no LLM call
            var result = _mapper(input);
            return Task.FromResult(result);
        }
    }
}
