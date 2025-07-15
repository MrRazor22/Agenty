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
    public class BuiltInTools
    {
        private readonly Dictionary<MethodInfo, Tool> _toolMap;

        public BuiltInTools(IToolRegistry registry, Type agentTool)
        {
            registry.RegisterAllFromType(agentTool);
            _toolMap = registry.GetAllTools()
                   .GroupBy(t => t.Function.Method)
                   .ToDictionary(g => g.Key, g => g.First());

        }

        public Tool Get(Delegate method)
        {
            var methodInfo = method.Method;
            return _toolMap.TryGetValue(methodInfo, out var tool)
                ? tool
                : throw new KeyNotFoundException($"No tool registered for method {methodInfo.Name}");
        }

    }


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
