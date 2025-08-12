using Agenty.LLMCore;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace Agenty.AgentCore
{
    public class AgentMemory
    {
        public string Goal { get; set; } = "";
        public Plan CurrentPlan { get; private set; } = new();
        public Scratchpad Scratchpad { get; private set; } = new();
        public FeedBack? CriticFeedback { get; set; }
        public ITools Tools { get; set; }

        public bool HasTools() => Tools?.RegisteredTools.Count > 0;

        public PlanStep? GetCurrentStep() => CurrentPlan.GetCurrentStep();

        public void AdvanceStep() => CurrentPlan.Advance();

        public bool IsPlanComplete() => CurrentPlan.IsComplete;

        public void AddScratchpadEntry(ScratchpadEntry entry) => Scratchpad.AddEntry(entry);

        public void UpdatePlan(Plan newPlan, int resumeFromStep = 0)
        {
            if (resumeFromStep <= 0 || resumeFromStep > CurrentPlan.Steps.Count)
            {
                CurrentPlan = newPlan;
                CurrentPlan.Reset(0);
                Scratchpad.Clear();
                return;
            }

            var keptSteps = CurrentPlan.Steps.Take(resumeFromStep).ToList();
            var updatedSteps = keptSteps.Concat(newPlan.Steps).ToList();

            CurrentPlan = new Plan();
            foreach (var step in updatedSteps)
                CurrentPlan.AddStep(step);

            CurrentPlan.Reset(resumeFromStep);
            Scratchpad.RemoveEntriesAfterStep(resumeFromStep);
        }
        public void UpdateFeedback(FeedBack feedback)
        {
            CriticFeedback = feedback;
        }

    }
}
