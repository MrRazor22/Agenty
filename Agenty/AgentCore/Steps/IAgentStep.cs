using Agenty.LLMCore;
using Agenty.LLMCore.ChatHandling;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Agenty.AgentCore.Steps
{
    public delegate Task AgentStepDelegate(IAgentContext ctx);

    public interface IAgentStep
    {
        Task InvokeAsync(IAgentContext ctx, AgentStepDelegate next);
    }


    /// <summary>
    /// Tracks the current step name using AsyncLocal for implicit context flow.
    /// Similar to how ASP.NET tracks HttpContext.
    /// </summary>
    public static class StepContext
    {
        public static readonly AsyncLocal<string?> Current = new AsyncLocal<string?>();
    }

}
