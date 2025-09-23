using Agenty.AgentCore.Runtime;
using Agenty.LLMCore;
using Agenty.LLMCore.ChatHandling;

namespace Agenty.AgentCore.Steps.Domain
{
    public sealed class SummarizationStep : IAgentStep<object, string>
    {
        private readonly string _systemInstruction;
        private readonly string? _goalOverride;

        public SummarizationStep(
            string? systemInstruction = null,
            string? goal = null)
        {
            _systemInstruction = systemInstruction ??
                @"Provide a concise direct final answer addressing the current USER QUESTION 
                  using only the CONTEXT. 
                  Always include relevant observations from the CONTEXT, even if incomplete. 
                  If information is missing, state the limitation clearly.";

            _goalOverride = goal;
        }

        public async Task<string?> RunAsync(IAgentContext ctx, object? input = null)
        {
            var chat = ctx.Memory.Working;
            var llm = ctx.LLM;

            var userRequest = _goalOverride ?? chat.GetCurrentUserRequest()
                ?? throw new InvalidOperationException("No user request found.");

            var scopedHistory = chat.GetScopedFromLastUser()
                                    .ToJson(ChatFilter.All & ~ChatFilter.System);

            var summary = await llm.GetStructured<string>(
                new Conversation()
                    .Add(Role.System, _systemInstruction)
                    .Add(Role.User, $"USER QUESTION: {userRequest}\nCONTEXT:\n{scopedHistory}"),
                LLMMode.Creative);

            if (!string.IsNullOrWhiteSpace(summary))
                chat.Add(Role.Assistant, summary);

            return summary;
        }
    }
}
