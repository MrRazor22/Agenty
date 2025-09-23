using Agenty.LLMCore;
using Agenty.LLMCore.ChatHandling;
using Microsoft.Extensions.Logging;

namespace Agenty.AgentCore.Runtime
{
    // === Generic step interface (context-first) ===
    public interface IAgentStep<TIn, TOut>
    {
        Task<TOut?> RunAsync(IAgentContext ctx, TIn? input = default);
    }

}
