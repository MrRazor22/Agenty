using Agenty.AgentCore.Runtime;
using Agenty.LLMCore;
using Agenty.LLMCore.ChatHandling;

namespace Agenty.AgentCore.Steps.Domain
{
    /// <summary>
    /// Step that finalizes the conversation with a user-friendly answer.
    /// </summary>
    public sealed class FinalizeStep : IAgentStep<string, string>
    {
        private readonly string _finalPrompt;

        public FinalizeStep(string finalPrompt = "Give a final user friendly answer.")
        {
            _finalPrompt = finalPrompt;
        }

        public async Task<string?> RunAsync(IAgentContext ctx, string? input = null)
        {
            var chat = ctx.Memory.Working;

            // If something was passed in, include it in the chat as neutral context
            if (!string.IsNullOrWhiteSpace(input))
            {
                chat.Add(Role.System, input);
            }

            // Always add the final user-facing instruction
            var response = await ctx.LLM.GetResponse(
                chat.Add(Role.User, _finalPrompt),
                LLMMode.Creative);

            if (!string.IsNullOrWhiteSpace(response))
                chat.Add(Role.Assistant, response);

            return response;
        }

    }
}
