using Agenty.LLMCore;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Agenty.AgentCore
{
    public class AgentTools
    {

        [Description("Creates a plan with ordered steps for the agent to follow.")]
        public static IPlan SetPlan(
                [Description("An ordered list of step descriptions.")] List<string> steps)
        {
            var plan = new Plan();
            foreach (var step in steps)
                plan.AddStep(step);
            return plan;
        }

        [Description("Return a structured result for the current step")]
        public static StepResult GiveStepResult(
            [Description("The full step result including outcome and message")]
        StepResult result)
        {
            return result;
        }
    }

}
