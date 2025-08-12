using System;
using System.Collections.Generic;
using System.Linq;
using Agenty.LLMCore;

namespace Agenty.AgentCore
{
    public interface IPrompts
    {
        Conversation BuildCreatePlanPrompt();
        Conversation BuildCreateRecoveryPlanPrompt(IReadOnlyList<ScratchpadEntry> failedContext);
        Conversation BuildExecuteStepPrompt();
        Conversation BuildCriticReviewPrompt();
    }

    public class Prompts : IPrompts
    {
        private readonly AgentMemory _memory;

        public Prompts(AgentMemory memory)
        {
            _memory = memory;
        }

        public Conversation BuildCreatePlanPrompt()
        {
            var convo = new Conversation();
            convo.Add(Role.User, $"Goal: {_memory.Goal}");
            convo.Add(Role.User, "Previous context:");
            AddContextMessages(convo, _memory.Scratchpad.Entries);
            convo.Add(Role.User, "Create a clear, ordered plan of steps to achieve the goal.");
            return convo;
        }

        public Conversation BuildCreateRecoveryPlanPrompt(IReadOnlyList<ScratchpadEntry> failedContext)
        {
            var convo = new Conversation();
            convo.Add(Role.User, $"Goal: {_memory.Goal}");
            convo.Add(Role.User, "The following steps led to failure or drift:");
            AddContextMessages(convo, failedContext);
            convo.Add(Role.User, "Generate a new plan to recover and achieve the goal.");
            return convo;
        }

        public Conversation BuildExecuteStepPrompt()
        {
            PlanStep step = _memory.GetCurrentStep() ?? throw new Exception("No current step");
            var convo = new Conversation();
            convo.Add(Role.User, $"Execute the following step: {step.Description}");
            if (_memory.HasTools())
                convo.Add(Role.User, $"Use tool: {step.ToolName}");
            convo.Add(Role.User, "Current context:");
            AddContextMessages(convo, _memory.Scratchpad.Entries);
            convo.Add(Role.User, "Provide action taken and insights from executing this step.");
            return convo;
        }

        public Conversation BuildCriticReviewPrompt()
        {
            var convo = new Conversation();
            convo.Add(Role.User, "Review the following agent context:");
            AddContextMessages(convo, _memory.Scratchpad.Entries);
            convo.Add(Role.User, "Is the agent's progress aligned with the goal? Provide true/false and reasoning.");
            return convo;
        }

        private void AddContextMessages(Conversation convo, IReadOnlyList<ScratchpadEntry> context)
        {
            if (context == null || !context.Any())
            {
                convo.Add(Role.User, "No previous context.");
                return;
            }

            int idx = 1;
            foreach (var entry in context)
            {
                convo.Add(Role.User, $"Step {idx} Action: {entry.ActionTaken}");
                convo.Add(Role.User, $"Step {idx} Insights: {entry.Insights}");
                idx++;
            }
        }
    }
}
