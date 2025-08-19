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
        Task<Plan> CreatePlanAsync(string goal, IToolRegistry userTools, Scratchpad context);
        Task<Plan> CreateUpdatedPlanAsync(string goal, Scratchpad context, FeedBack feedBack);
    }

    public interface IExecutor
    {
        Task<ScratchpadEntry> ExecuteStepAsync(string goal, PlanStep step, Scratchpad context);
    }

    public interface ICritic
    {
        Task<FeedBack> ReviewAsync(Scratchpad context);
    }


}
