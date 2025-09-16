using Agenty.LLMCore.ChatHandling;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Agenty.AgentCore.Executors
{
    internal class SimpleChatBotExecutor : IExecutor
    {
        public async Task<string> ExecuteAsync(IAgentContext context, string goal)
        {
            var chat = new Conversation().Append(context.Conversation);
            context.Logger.AttachTo(chat);

            chat.Add(Role.System, "You are a friendly assistant. Answer in <=3 sentences.");
            return await context.LLM.GetResponse(chat);
        }
    }
}
