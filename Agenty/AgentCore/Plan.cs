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
    }

    public class PlanStep
    {
        [Description("Text describing the step to be executed.")]
        public string Description { get; set; } = "";

        [Description("Optional: Name of the tool intended to execute this step.")]
        public string? ToolName { get; set; }
    }

}
