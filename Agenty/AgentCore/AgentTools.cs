using Agenty.LLMCore;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Agenty.AgentCore
{
    public class AgentTools
    {

        [Description("Creates a new plan with ordered steps for the agent to follow.")]
        public static Plan CreatePlan(
    [Description("An ordered list of step descriptions.")] List<PlanStep> steps)
        {
            var plan = new Plan();
            foreach (var step in steps) plan.AddStep(step);
            return plan;
        }

        [Description("Updates the scratchpad with the latest action and insights from the agent.")]
        public static ScratchpadEntry UpdateScratchpad(
    [Description("The latest scratchpad entry with action and insights")] ScratchpadEntry entry)
        {
            return entry;
        }

        [Description("Evaluates the current progress and returns feedback on alignment with the goal.")]
        public static FeedBack EvaluateProgress(
    [Description("Text summary of current progress and context")] string progressContext)
        {
            // The LLM should analyze the provided context and return a FeedBack object.
            return new FeedBack();
        }

    }

}
