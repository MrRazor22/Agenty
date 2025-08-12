using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Agenty.AgentCore
{
    public class Agent : IAgent
    {
        private readonly IPlanner _planner;
        private readonly IExecutor _executor;
        private readonly ICritic _critic;
        private readonly Prompts _prompts;
        private readonly AgentMemory _memory = new();
        private int stepsSinceLastCritique = 0;
        private int critiqueInterval = 3; // example, critique every 3 steps
        private int lastGoodStepIndex = 0;

        public Agent(IPlanner planner, IExecutor executor, ICritic critic, Prompts? prompts = null)
        {
            _prompts = prompts ?? new Prompts(_memory);
            _planner = planner;
            _executor = executor;
            _critic = critic;
        }

        public async Task<string> ExecuteAsync(string goal)
        {
            _memory.Goal = goal;

            var plan = await _planner.CreatePlanAsync(goal, _memory.Scratchpad.Entries);
            _memory.UpdatePlan(plan);

            while (!_memory.IsPlanComplete())
            {
                var step = _memory.GetCurrentStep();
                if (step == null) break;

                var scratchEntry = await _executor.ExecuteStepAsync(step, _memory.Scratchpad.Entries);
                _memory.AddScratchpadEntry(scratchEntry);

                stepsSinceLastCritique++;

                if (stepsSinceLastCritique >= critiqueInterval)
                {
                    var feedback = await _critic.ReviewAsync(_memory.Scratchpad.Entries);
                    _memory.UpdateFeedback(feedback);

                    if (feedback.IsAlignedWithGoal)
                    {
                        lastGoodStepIndex = _memory.CurrentPlan.CurrentStepIndex;
                    }
                    else
                    {
                        var newPlan = await _planner.CreateRecoveryPlanAsync(goal, _memory.Scratchpad.Entries);
                        _memory.UpdatePlan(newPlan, lastGoodStepIndex);
                        stepsSinceLastCritique = 0;
                        continue;
                    }

                    stepsSinceLastCritique = 0;
                }

                _memory.AdvanceStep();
            }

            return _memory.Scratchpad.Entries.LastOrDefault()?.Insights ?? "No result";
        }
    }


}
