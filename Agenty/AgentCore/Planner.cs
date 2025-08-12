using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Agenty.AgentCore
{
    internal class Planner : IPlanner
    {
        public Task<Plan> CreatePlanAsync(string goal, IReadOnlyList<ScratchpadEntry> context)
        {
            throw new NotImplementedException();
        }

        public Task<Plan> CreateRecoveryPlanAsync(string goal, IReadOnlyList<ScratchpadEntry> failedContext)
        {
            throw new NotImplementedException();
        }
    }
}
