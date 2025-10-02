using Agenty.LLMCore;
using Agenty.LLMCore.ChatHandling;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace Agenty.AgentCore.Runtime
{
    // === Generic step interface (context-first) ===
    public interface IAgentStep
    {
        Task<string> RunAsync(IAgentContext ctx);
    }
}
