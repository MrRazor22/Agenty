using Agenty.LLMCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Agenty.AgentCore
{
    public class Planner(ILLMClient llm) : IPlanner
    {
        public async Task<IPlan?> GeneratePlan(IAgentMemory memory, string goal)
        {
            var prompt = $"Plan how to achieve this goal: \"{goal}\". " +
                         "Think step-by-step and list short, single-sentence steps." +
                         "If not a real goal or task to accomplish create a simple plan just to give a response";

            memory.ChatHistory.Add(ChatRole.Assistant, prompt);
            var response = await llm.GetResponse(memory.ChatHistory);
            memory.ChatHistory.Add(ChatRole.Assistant, response);
            memory.Scratchpad.Add($"Initial plan response:\n{response}");

            var schema = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["steps"] = new JsonObject
                    {
                        ["type"] = "array",
                        ["items"] = new JsonObject { ["type"] = "string" }
                    }
                },
                ["required"] = new JsonArray { "steps" }
            };

            var result = llm.GetStructuredResponse(memory.ChatHistory, schema);
            var steps = result?["steps"]?.AsArray();
            if (steps == null || steps.Count == 0) return null;

            var plan = new Plan();
            foreach (var step in steps)
                plan.AddStep(step?.ToString() ?? "");

            return plan;
        }

        public async Task<IPlan?> RefinePlan(IAgentMemory memory, IPlan existingPlan, string feedback)
        {
            var planText = string.Join("\n", existingPlan.Steps.Select((s, i) => $"{i + 1}. {s.Description}"));
            var prompt = $"Here's the current plan:\n{planText}\n\nFeedback: {feedback}\n\n" +
                         "Refine the plan based on this feedback and output only the updated steps.";

            memory.ChatHistory.Add(ChatRole.Assistant, prompt);
            var response = await llm.GetResponse(memory.ChatHistory);
            memory.ChatHistory.Add(ChatRole.Assistant, response);
            memory.Scratchpad.Add($"Refined plan:\n{response}");

            var schema = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["steps"] = new JsonObject
                    {
                        ["type"] = "array",
                        ["items"] = new JsonObject { ["type"] = "string" }
                    }
                },
                ["required"] = new JsonArray { "steps" }
            };

            var result = llm.GetStructuredResponse(memory.ChatHistory, schema);
            var steps = result?["steps"]?.AsArray();
            if (steps == null || steps.Count == 0) return null;

            var newPlan = new Plan();
            foreach (var step in steps)
                newPlan.AddStep(step?.ToString() ?? "");

            return newPlan;
        }
    }

}
