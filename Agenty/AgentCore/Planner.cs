using Agenty.LLMCore;
using System.Text.Json.Nodes;

namespace Agenty.AgentCore;

public class Planner : IPlanner
{
    private readonly ILLMClient _llm;
    private readonly IAgentMemory _memory;
    private readonly IAgentLogger? _logger;

    public Planner(ILLMClient llm, IAgentMemory agentMemory, IAgentLogger? logger = null)
    {
        _llm = llm;
        _memory = agentMemory;
        _logger = logger;
    }

    public Action<IPlan>? OnPlanUpdated { get; set; }

    public async Task<IPlan?> GeneratePlan(string goal, List<Tool> availableTools = null)
    {
        var toolInfo = (availableTools != null && availableTools.Count > 0)
        ? $"You have the following tools available: {string.Join("\n", availableTools.Select(t => t.ToString()))}.\n"
        : "";

        var prompt = $"""
        Plan how to achieve this goal: \"{goal}\".
        {toolInfo}Think step-by-step and list short, single-sentence steps.
        """;

        _memory.ChatHistory.Add(ChatRole.Assistant, prompt);
        var response = await _llm.GetResponse(_memory.ChatHistory);
        _memory.ChatHistory.Add(ChatRole.Assistant, response); 

        var steps = ParseSteps(await _llm.GetStructuredResponse(_memory.ChatHistory, PlanSchema()));
        if (steps == null) return null;

        var plan = new Plan(steps);
        OnPlanUpdated?.Invoke(plan);
        _logger?.Log("Planner", $"[Plan]: {string.Join("\n", plan.Steps.Select(s => s.Description))}");

        return plan;
    }

    public async Task<IPlan?> RefinePlan(IPlan existingPlan, string feedback, List<Tool> availableTools = null)
    {
        var toolInfo = (availableTools != null && availableTools.Count > 0)
        ? $"You have the following tools available: {string.Join("\n", availableTools.Select(t => t.ToString()))}.\n"
        : "";

        var planText = string.Join("\n", existingPlan.Steps.Select((s, i) => $"{i + 1}. {s.Description}"));
        var prompt = $"Here's the current plan:\n{planText}\n\nFeedback: {feedback}\n\n" + toolInfo +
                     "Refine the plan based on this feedback and output only the updated steps.";

        _memory.ChatHistory.Add(ChatRole.Assistant, prompt);
        var response = await _llm.GetResponse(_memory.ChatHistory);
        _memory.ChatHistory.Add(ChatRole.Assistant, response); 

        var steps = ParseSteps(await _llm.GetStructuredResponse(_memory.ChatHistory, PlanSchema()));
        if (steps == null) return null;

        var plan = new Plan(steps);
        OnPlanUpdated?.Invoke(plan);
        _logger?.Log("Planner", $"[Refined plan]:\n{string.Join("\n", plan.Steps.Select(s => s.Description))}");

        return plan;
    }

    private static List<string>? ParseSteps(JsonObject? parsed)
    {
        var arr = parsed?["steps"]?.AsArray();
        return arr?.Select(s => s?.ToString() ?? "").Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
    }

    private static JsonObject PlanSchema() => new()
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
}
