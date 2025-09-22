using Agenty.LLMCore.ChatHandling;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Agenty.AgentCore.Executors
{
    /// <summary>
    /// Dead simple chatbot: just ask the LLM for a response.
    /// </summary>
    public sealed class SimpleChatBotExecutor : IExecutor
    {
        public async Task<object?> Execute(IAgentContext ctx)
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
