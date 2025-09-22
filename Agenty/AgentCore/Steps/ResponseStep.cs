using Agenty.AgentCore.Runtime;
using Agenty.LLMCore;
using Agenty.LLMCore.ChatHandling;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Agenty.AgentCore.Steps
{
    public sealed class ResponseStep : IAgentStep<object, string>
    {
        public async Task<string?> RunAsync(Conversation chat, ILLMCoordinator llm, object? input = null)
        {
            var resp = await llm.GetResponse(chat, LLMMode.Balanced);
            if (!string.IsNullOrWhiteSpace(resp))
                chat.Add(Role.Assistant, resp);
            return resp;
        }
    }

}
