using Agenty.AgentCore.Runtime;
using Agenty.LLMCore;
using Agenty.LLMCore.ChatHandling;

namespace Agenty.AgentCore.Steps
{
    /// <summary>
    /// Step that finalizes the conversation with a user-friendly answer.
    /// </summary>
    public sealed class FinalizeStep : IAgentStep<string, string>
    {
        private readonly string _finalPrompt;

        public FinalizeStep(
            string finalPrompt = "Wrap up the conversation with a clear, user-facing answer.")
        {
            _finalPrompt = finalPrompt;
        }

        public async Task<string?> RunAsync(IAgentContext ctx, string? input = null)
        {
            var chat = ctx.Memory.Working;

            var goal = chat.GetCurrentUserRequest() ?? "the user request";

            var candidate = string.IsNullOrWhiteSpace(input)
                ? "(no candidate answer produced)"
                : input;

            var refined = await ctx.LLM.GetResponse(
                new Conversation(chat.Where(m => m.Role != Role.System)) // filter inline
                    .Add(Role.System, _finalPrompt)
                    .Add(Role.User,
                        $"USER GOAL: {goal}\n" +
                        $"CANDIDATE ANSWER: {candidate}\n\n" +
                        "Return only the final, user-facing answer in a clear and concise way."),
                LLMMode.Creative);

            if (!string.IsNullOrWhiteSpace(refined))
                chat.Add(Role.Assistant, refined);

            return refined;
        }

    }

}

