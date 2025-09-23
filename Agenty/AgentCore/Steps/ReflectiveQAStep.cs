using Agenty.AgentCore.Runtime;
using Agenty.LLMCore.ChatHandling;

namespace Agenty.AgentCore.Steps
{
    public sealed class ReflectiveQAStep : IAgentStep<object, string>
    {
        private readonly string _finalPrompt;

        public ReflectiveQAStep(string finalPrompt = "Give a final user friendly answer.")
        {
            _finalPrompt = finalPrompt;
        }

        public async Task<string?> RunAsync(IAgentContext ctx, object? input = null)
        {
            var chat = ctx.Memory.Working;
            var llm = ctx.LLM;

            // 1. Summarize
            var summary = await new SummarizationStep().RunAsync(ctx, input);

            // 2. Evaluate
            var verdict = await new EvaluationStep().RunAsync(ctx, summary);

            if (verdict?.confidence_score is Verdict.yes or Verdict.partial)
            {
                if (verdict.confidence_score == Verdict.partial)
                    chat.Add(Role.Assistant, verdict.explanation);

                // 3. Finalize
                return await new FinalizeStep(_finalPrompt).RunAsync(ctx, summary);
            }

            // 3. Replan
            await new ReplanningStep().RunAsync(ctx, verdict);
            return null; // loop will retry
        }
    }
}
