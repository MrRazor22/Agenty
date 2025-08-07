using Agenty.AgentCore;
using Agenty.LLMCore;

public class Executor : IExecutor
{
    private readonly ILLMClient _llmClient;
    private readonly IPlanner _planner;
    private readonly IAgentMemory _agentMemory;
    private readonly ITools _toolRegistry;
    private readonly ITools _builtInTools;
    private readonly IAgentLogger? _logger;

    public Func<Tool, Task<string>>? OnToolInvoking { get; set; }
    public Action<string>? OnFinalResponseReady { get; set; }

    public Executor(
        ILLMClient llmClient,
        IPlanner planner,
        IAgentMemory agentMemory,
        ITools toolRegistry,
        ITools builtInTools,
        IAgentLogger? logger = null)
    {
        _llmClient = llmClient;
        _planner = planner;
        _agentMemory = agentMemory;
        _toolRegistry = toolRegistry;
        _builtInTools = builtInTools;
        _logger = logger;

        OnToolInvoking = info =>
        {
            _logger?.Log("Executor", $"[Tool Call]: '{info.Name}' with: {info.Arguments}");
            return Task.FromResult("");
        };

        OnFinalResponseReady = res =>
        {
            _logger?.Log("Executor", $"[Response]: {res}");
        };
    }

    public async Task<string> Execute(string goal)
    {
        return "";
    }
}