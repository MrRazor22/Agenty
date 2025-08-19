using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Agenty.AgentCore
{
    public class Plan
    {
        private readonly List<PlanStep> _steps = new();
        public IReadOnlyList<PlanStep> Steps => _steps;
        public int CurrentStepIndex { get; private set; } = 0;

        public bool IsComplete => CurrentStepIndex >= _steps.Count;

        public void AddStep(PlanStep step) => _steps.Add(step);
        public PlanStep? GetCurrentStep()
        {
            return !IsComplete ? _steps[CurrentStepIndex] : null;
        }

        public void Advance()
        {
            if (!IsComplete) CurrentStepIndex++;
        }

        public void Reset(int stepIndex = 0)
        {
            CurrentStepIndex = stepIndex;
        }
        public override string ToString()
        {
            if (_steps.Count == 0) return "Plan is empty.";

            var sb = new StringBuilder();
            sb.AppendLine("Plan:");
            for (int i = 0; i < _steps.Count; i++)
            {
                var step = _steps[i];
                string marker = i == CurrentStepIndex ? "👉 " : "   ";
                sb.AppendLine($"{marker}{i + 1}. {step.Description} (Tool: {step.ToolName})");
            }

            return sb.ToString();
        }
    }

    [Description("Represents a single actionable step in the execution plan.")]
    public class PlanStep
    {
        [Description("Clear instruction for what to do in this step, including how to use a specific tool if relevant.")]
        public string Description { get; set; } = "";

        [Description("The exact name of the tool that should be used to perform this step. Leave empty if no tool is required.")]
        public string ToolName { get; set; } = "";
    }
}
