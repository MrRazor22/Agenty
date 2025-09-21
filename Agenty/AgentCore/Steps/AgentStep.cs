using Agenty.LLMCore;
using Agenty.LLMCore.ChatHandling;
using Agenty.LLMCore.JsonSchema;
using Agenty.LLMCore.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Agenty.AgentCore.Steps
{
    // Generic step interface
    public interface IAgentStep<TIn, TOut>
    {
        Task<StepResult<TOut>?> RunAsync(
            Conversation chat,
            ILLMOrchestrator llm,
            TIn? input = default);
    }

    // For steps that don’t need an input at all
    public interface IAgentStep<TOut>
    {
        Task<StepResult<TOut>?> RunAsync(
            Conversation chat,
            ILLMOrchestrator llm);
    }

    // Strongly typed result
    public record StepResult<T>(bool Continue, T? Payload = default);

    // === Example Steps ===
    public enum Verdict { no, partial, yes }
    public record Answer(Verdict confidence_score, string explanation);
    public sealed class EvaluationStep : IAgentStep<string, Answer>
    {
        private readonly string _goal;
        public EvaluationStep(string goal) => _goal = goal;

        public async Task<StepResult<Answer>?> RunAsync(
            Conversation chat, ILLMOrchestrator llm, string? input = null)
        {
            var response = input ?? chat.LastOrDefault(c => c.Role == Role.Assistant)
                                       ?.Content?.AsJSONString() ?? "";

            var verdict = await llm.GetStructured<Answer>(
                new Conversation()
                    .Add(Role.System, "You are a strict evaluator...")
                    .Add(Role.User, $"USER REQUEST: {_goal}\n RESPONSE: {response}"),
                LLMMode.Deterministic);

            chat.Add(Role.Assistant, new TextContent($"Verdict: {verdict.confidence_score}"));

            return new StepResult<Answer>(verdict.confidence_score == Verdict.yes, verdict);
        }
    }

    public sealed class SummarizationStep : IAgentStep<string>
    {
        private readonly string _userRequest;
        public SummarizationStep(string userRequest) => _userRequest = userRequest;

        public async Task<StepResult<string>?> RunAsync(
            Conversation chat, ILLMOrchestrator llm)
        {
            var summary = await llm.GetStructured<string>(
                new Conversation()
                    .Add(Role.System, "Provide a concise direct final answer...")
                    .Add(Role.User, $"USER QUESTION: {_userRequest}\nCONTEXT:\n{chat.ToJson(~ChatFilter.System)}"),
                LLMMode.Creative);

            chat.Add(Role.Assistant, new TextContent(summary));

            return new StepResult<string>(false, summary);
        }
    }

    public sealed class FinalizeStep : IAgentStep<string, string>
    {
        public async Task<StepResult<string>?> RunAsync(
            Conversation chat,
            ILLMOrchestrator llm,
            string? input = null)
        {
            var response = await llm.GetResponse(
                chat.Add(Role.User, "Give a final user friendly answer."),
                LLMMode.Creative);

            if (!string.IsNullOrWhiteSpace(response))
                chat.Add(Role.Assistant, response);

            return new StepResult<string>(false, response);
        }
    }


    // BranchStep: picks pipeline based on typed predicate
    public sealed class BranchStep<TIn, TOut> : IAgentStep<TIn, TOut>
    {
        private readonly Func<TIn?, bool> _predicate;
        private readonly StepExecutor _onTrue;
        private readonly StepExecutor _onFalse;

        public BranchStep(Func<TIn?, bool> predicate,
                          StepExecutor onTrue,
                          StepExecutor onFalse)
        {
            _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
            _onTrue = onTrue ?? throw new ArgumentNullException(nameof(onTrue));
            _onFalse = onFalse ?? throw new ArgumentNullException(nameof(onFalse));
        }

        public async Task<StepResult<TOut>?> RunAsync(
            Conversation chat, ILLMOrchestrator llm, TIn? input = default)
        {
            var branch = _predicate(input) ? _onTrue : _onFalse;
            var result = await branch.Execute(chat, llm);

            if (result is TOut typed)
                return new StepResult<TOut>(true, typed);

            throw new InvalidCastException(
                $"Branch returned {result?.GetType().Name ?? "null"}, expected {typeof(TOut).Name}");
        }
    }

    // === StepExecutor ===
    public sealed class StepExecutor : IExecutor
    {
        private readonly Func<Conversation, ILLMOrchestrator, Task<object?>> _fn;

        private StepExecutor(Func<Conversation, ILLMOrchestrator, Task<object?>> fn)
        {
            _fn = fn;
        }

        // Entry point for agent framework
        public Task<object?> Execute(IAgentContext ctx)
            => Execute(ctx.Memory.Working, ctx.LLM);

        // Internal pipeline entry
        internal Task<object?> Execute(Conversation chat, ILLMOrchestrator llm)
            => _fn(chat, llm);

        // --- Fluent Builder ---
        public sealed class Builder
        {
            private Func<Conversation, ILLMOrchestrator, Task<object?>>? _pipeline;

            public Builder Add<TIn, TOut>(IAgentStep<TIn, TOut> step)
            {
                var prev = _pipeline;
                _pipeline = async (chat, llm) =>
                {
                    object? input = prev == null ? default : await prev(chat, llm);

                    if (input is not TIn typed && input != null)
                    {
                        throw new InvalidCastException(
                            $"Pipeline type mismatch in step {step.GetType().Name}: " +
                            $"expected input {typeof(TIn).Name}, but received {input.GetType().Name} " +
                            $"with value {input}"
                        );
                    }

                    var result = await step.RunAsync(chat, llm, (TIn?)input);
                    if (result == null) return null;
                    if (!result.Continue) return result.Payload;
                    return result.Payload;
                };
                return this;
            }

            public Builder Add<TOut>(IAgentStep<TOut> step)
            {
                var prev = _pipeline;
                _pipeline = async (chat, llm) =>
                {
                    _ = prev == null ? null : await prev(chat, llm);

                    var result = await step.RunAsync(chat, llm);
                    if (result == null) return null;
                    if (!result.Continue) return result.Payload;
                    return result.Payload;
                };
                return this;
            }

            public Builder Branch<TIn, TOut>(Func<TIn?, bool> predicate,
                                             Action<Builder> onTrue,
                                             Action<Builder> onFalse)
            {
                var trueBuilder = new Builder();
                onTrue(trueBuilder);

                var falseBuilder = new Builder();
                onFalse(falseBuilder);

                var branchStep = new BranchStep<TIn, TOut>(
                    predicate,
                    trueBuilder.Build(),
                    falseBuilder.Build()
                );

                return Add(branchStep);
            }

            public StepExecutor Build()
            {
                if (_pipeline == null)
                    throw new InvalidOperationException("Pipeline is empty");
                return new StepExecutor(_pipeline);
            }
        }
    }
}
