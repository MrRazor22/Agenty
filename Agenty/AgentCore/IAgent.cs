using Agenty.LLMCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Agenty.AgentCore
{
    public interface IAgent
    {
        Task<string> ExecuteAsync(string goal);
    }

    public interface IPlanner
    {
        Task<Plan> CreatePlanAsync(string goal, IReadOnlyList<ScratchpadEntry> context);
        Task<Plan> CreateRecoveryPlanAsync(string goal, IReadOnlyList<ScratchpadEntry> failedContext);
    }

    public interface IExecutor
    {
        Task<ScratchpadEntry> ExecuteStepAsync(PlanStep step, IReadOnlyList<ScratchpadEntry> context);
    }

    public interface ICritic
    {
        Task<FeedBack> ReviewAsync(IReadOnlyList<ScratchpadEntry> context);
    }


}
