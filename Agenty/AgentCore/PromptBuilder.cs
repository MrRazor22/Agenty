using Agenty.LLMCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Agenty.AgentCore
{
    public abstract class PromptBuilder
    {
        public virtual Prompt BuildPlanPrompt(string goal, List<Tool>? availableTools = null)
        {
            return new Prompt {
            { ChatRole.System, "You are a helpful agent designed to plan and execute goals using provided tools." },
            { ChatRole.User, $"Goal: {goal}" },
            { ChatRole.User, GetBasicToolDescription(availableTools) },
            { ChatRole.User, $"""
                If the goal is trivial or just needs a direct reply, don’t plan—just say what to do.
                If it’s more complex, break it into short, clear steps.
                """ }
        };
        }

        private static string GetBasicToolDescription(List<Tool>? availableTools)
        {
            return (availableTools != null && availableTools.Count > 0)
                ? $"You have the following tools available:\n{string.Join("\n", availableTools.Select(t => t.ToString()))}\n"
                : "";
        }

        public virtual Prompt BuildStepPrompt(string goal, List<string> tools, List<string> plan, string currentStep)
        {
            var toolsText = string.Join(", ", tools);
            var planText = string.Join("\n", plan.Select((step, i) => $"{i + 1}. {step}"));

            return new Prompt {
            { ChatRole.System, "You're executing a step from a plan. Choose the next action." },
            { ChatRole.User, $"Goal: {goal}" },
            { ChatRole.User, $"Plan:\n{planText}" },
            { ChatRole.User, $"Current step: {currentStep}" },
            { ChatRole.User, $"Available tools: {toolsText}" },
            { ChatRole.User, "What will you do next?" }
            };
        }

        public virtual Prompt BuildStepResultReflectionPrompt(string currentStep, string? toolResult, string goal, List<string> plan)
        {
            var planText = string.Join("\n", plan.Select((step, i) => $"{i + 1}. {step}"));

            return new Prompt {
            { ChatRole.System, "Reflect on the result of the last step." },
            { ChatRole.User, $"Step executed: {currentStep}" },
            { ChatRole.User, $"Tool result:\n{toolResult}" },
            { ChatRole.User, $"Goal: {goal}" },
            { ChatRole.User, $"Plan:\n{planText}" },
            { ChatRole.User, "Did the step succeed? What should happen next?" }
        };
        }

        public virtual Prompt BuildRefinePlanPrompt(string goal, IPlan oldPlan, string modelFeedback, List<Tool>? availableTools = null)
        {
            return new Prompt {
            { ChatRole.System, $"You're improving the plan for the Goal: {goal} based on model feedback." },
            { ChatRole.User, GetBasicToolDescription(availableTools) },
            { ChatRole.User, $"Original plan:\n{oldPlan}" },
            { ChatRole.User, $"Feedback:\n{modelFeedback}" },
            { ChatRole.User, "Update the plan accordingly." }
        };
        }
    }
    public class StandardPromptBuilder : PromptBuilder
    {
        // No overrides — inherits base behavior as-is
    }
}

