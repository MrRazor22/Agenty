using Agenty.LLMCore;
using Agenty.LLMCore.ChatHandling;

namespace Agenty.AgentCore.Steps
{
    public sealed class SummarizationStep : IAgentStep<object, string>
    {
        public async Task<string?> RunAsync(
            Conversation chat, ILLMOrchestrator llm, object? input = null)
        {
            var userRequest = chat.GetLastUserMessage()
                ?? throw new InvalidOperationException("No user request found.");

            var scopedHistory = chat.GetScopedFromLastUser().ToJson(ChatFilter.All & ~ChatFilter.System);

            var summary = await llm.GetStructured<string>(
                new Conversation()
                    .Add(Role.System,
                        @"Provide a concise direct final answer addressing the current USER QUESTION 
                      using only the CONTEXT. 
                      Always include relevant observations from the CONTEXT, even if incomplete. 
                      If information is missing, state the limitation clearly.")
                    .Add(Role.User, $"USER QUESTION: {userRequest}\nCONTEXT:\n{scopedHistory}"),
                LLMMode.Creative);

            if (!string.IsNullOrWhiteSpace(summary))
                chat.Add(Role.Assistant, summary);

            return summary;
        }
    }
}
