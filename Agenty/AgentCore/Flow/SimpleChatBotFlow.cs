using Agenty.AgentCore.Runtime;
using Agenty.LLMCore.ChatHandling;
using System.Threading.Tasks;

namespace Agenty.AgentCore.Flows
{
    /// <summary>
    /// Dead simple chatbot: just ask the LLM for a response.
    /// </summary>
    public sealed class SimpleChatBotFlow : IAgentStep<object, string>
    {
        public async Task<string?> RunAsync(IAgentContext ctx, object? input = null)
        {
            var chat = ctx.Memory.Working;
            ctx.Logger.AttachTo(chat);

            var resp = await ctx.LLM.GetResponse(chat);
            if (!string.IsNullOrWhiteSpace(resp))
                chat.Add(Role.Assistant, resp);

            return resp;
        }
    }
}
