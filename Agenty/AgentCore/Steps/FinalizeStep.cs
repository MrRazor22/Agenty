using Agenty.AgentCore.Runtime;
using Agenty.LLMCore;
using Agenty.LLMCore.ChatHandling;

namespace Agenty.AgentCore.Steps
{
    public sealed class FinalizeStep : IAgentStep<string, string>
    {
        public async Task<string?> RunAsync(
            Conversation chat,
            ILLMCoordinator llm,
            string? input = null)
        {
            var response = await llm.GetResponse(
                chat.Add(Role.User, "Give a final user friendly answer."),
                LLMMode.Creative);

            if (!string.IsNullOrWhiteSpace(response))
                chat.Add(Role.Assistant, response);

            return response;
        }
    }
}
