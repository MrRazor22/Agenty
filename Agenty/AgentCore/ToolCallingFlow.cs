using Agenty.AgentCore.Runtime;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Agenty.AgentCore
{
    internal class ToolCallingFlow : IAgentStep
    {
        Task<string> IAgentStep.RunAsync(IAgentContext ctx)
        {
            throw new NotImplementedException();
        }
    }
}
