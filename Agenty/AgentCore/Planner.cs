using Agenty.LLMCore;
using System.Text.Json.Nodes;

namespace Agenty.AgentCore;

public class Planner : IPlanner
{
    private readonly ILLMClient _llm;
    private readonly IAgentMemory _memory;
    private readonly IToolManager _builtInTools;
    private readonly IAgentLogger? _logger;


    public Planner(
        ILLMClient llm,
        IAgentMemory agentMemory,
        IToolManager toolRegistry,
        IToolManager builtInTools,
        IAgentLogger? logger = null)
    {
        _llm = llm;
        _memory = agentMemory;
        _builtInTools = builtInTools;
        _logger = logger;
    }

    public Action<IPlan>? OnPlanUpdated { get; set; }

    //public async Task<IPlan?> GeneratePlan(List<Tool>? availableTools = null)
    //{
    //    var prompt = _promptBuilder.BuildPlanPrompt(_memory.Goal, availableTools);

    //    var toolCallResponse = await _llm.GetToolCallResponse(prompt, true, _builtInTools.Get(AgentTools.SetPlan));

    //    if (toolCallResponse.AssistantMessage != null)
    //    {
    //        _memory.Thoughts.Add(toolCallResponse.AssistantMessage);
    //        return null;
    //    }

    //    var plan = _agentToolExecutor.InvokeTypedTool<IPlan>(toolCallResponse.ToolCalls[0]);
    //    if (plan == null) return null;

    //    OnPlanUpdated?.Invoke(plan);
    //    _logger?.Log("Planner", $"[Plan]: {plan}");
    //    return plan;
    //}


    //public async Task<IPlan?> RefinePlan(string feedback, List<Tool>? availableTools = null)
    //{
    //    var oldPlan = string.Join("\n", _memory.Plan.StepsWithResult.Select((s, i) => $"{i + 1}. {s.step}"));
    //    var prompt = _promptBuilder.BuildRefinePlanPrompt(_memory.Goal, _memory.Plan, feedback, availableTools);

    //    var toolCallResponse = await _llm.GetToolCallResponse(prompt, true, _builtInTools.Get(AgentTools.SetPlan));
    //    if (toolCallResponse.AssistantMessage != null)
    //    {
    //        _memory.Thoughts.Add(toolCallResponse.AssistantMessage);
    //        return null;
    //    }

    //    var plan = _agentToolExecutor.InvokeTypedTool<IPlan>(toolCallResponse.ToolCalls[0]);
    //    if (plan == null) return null;

    //    OnPlanUpdated?.Invoke(plan);
    //    _logger?.Log("Planner", $"[Refined plan]:\n{string.Join("\n", plan.StepsWithResult.Select(s => s.step))}");
    //    return plan;
    //}

}
