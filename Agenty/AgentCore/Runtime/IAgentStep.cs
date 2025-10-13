using Agenty.LLMCore;
using Agenty.LLMCore.ChatHandling;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Agenty.AgentCore.Runtime
{
    public delegate Task AgentStepDelegate(IAgentContext ctx);

    public interface IAgentStep
    {
        Task InvokeAsync(IAgentContext ctx, AgentStepDelegate next);
    }

}
