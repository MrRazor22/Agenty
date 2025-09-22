using Agenty.AgentCore.Runtime;
using Agenty.LLMCore.ChatHandling;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Agenty.AgentCore.Steps
{
    // === StepExecutor === 
    public sealed class StepExecutor : IExecutor
    {
        private readonly Func<Conversation, ILLMCoordinator, ILogger?, Task<object?>> _fn;
        private StepExecutor(Func<Conversation, ILLMCoordinator, ILogger?, Task<object?>> fn) => _fn = fn;

        public Task<object?> Execute(IAgentContext ctx)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (ctx.Memory?.Working == null) throw new ArgumentNullException(nameof(ctx.Memory.Working));
            if (ctx.LLM == null) throw new ArgumentNullException(nameof(ctx.LLM));
            return Execute(ctx.Memory.Working, ctx.LLM, ctx.Logger);
        }

        internal Task<object?> Execute(Conversation chat, ILLMCoordinator llm, ILogger? logger = null)
            => _fn(chat ?? throw new ArgumentNullException(nameof(chat)),
                   llm ?? throw new ArgumentNullException(nameof(llm)),
                   logger);

        // --- Fluent Builder ---
        public sealed class Builder
        {
            private Func<Conversation, ILLMCoordinator, ILogger?, Task<object?>>? _pipeline;

            public Builder Add<TIn, TOut>(IAgentStep<TIn, TOut> step)
            {
                var prev = _pipeline;
                _pipeline = async (chat, llm, logger) =>
                {
                    object? input = prev == null ? default : await prev(chat, llm, logger);

                    if (input is not TIn typed && input != null)
                        throw new InvalidCastException(
                            $"Pipeline type mismatch in {step.GetType().Name}: " +
                            $"expected {typeof(TIn).Name}, got {input.GetType().Name} with value {input}");

                    return await RunStep(step, (TIn?)input, chat, llm, logger);
                };
                return this;
            }

            private static async Task<TOut?> RunStep<TIn, TOut>(
                IAgentStep<TIn, TOut> step, TIn? input,
                Conversation chat, ILLMCoordinator llm, ILogger? logger)
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    logger?.LogDebug("Running {Step} with input {InputType}", step.GetType().Name, typeof(TIn).Name);
                    var result = await step.RunAsync(chat, llm, input);
                    logger?.LogDebug("Step {Step} completed in {Elapsed}ms -> {OutputType}",
                        step.GetType().Name, sw.ElapsedMilliseconds, typeof(TOut).Name);
                    return result;
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "Step {Step} failed", step.GetType().Name);
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
