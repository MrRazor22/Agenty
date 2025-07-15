using Agenty.AgentCore;
using Agenty.LLMCore;

public class Executor : IExecutor
{
    private readonly ILLMClient _llmClient;
    private readonly IPlanner _planner;
    private readonly IAgentMemory _agentMemory;
    private readonly IToolExecutor _toolExecutor;
    private readonly IToolRegistry _toolRegistry;
    private readonly PromptBuilder _promptBuilder;
    private readonly BuiltInTools _builtInTools;
    private readonly IAgentLogger? _logger;

    public Func<ToolCallInfo, Task<string>>? OnToolInvoking { get; set; }
    public Action<string>? OnFinalResponseReady { get; set; }

    public Executor(
        ILLMClient llmClient,
        IPlanner planner,
        IAgentMemory agentMemory,
        IToolExecutor toolExecutor,
        IToolRegistry toolRegistry,
        PromptBuilder promptBuilder,
        BuiltInTools builtInTools,
        IAgentLogger? logger = null)
    {
        _llmClient = llmClient;
        _planner = planner;
        _agentMemory = agentMemory;
        _toolExecutor = toolExecutor;
        _toolRegistry = toolRegistry;
        _promptBuilder = promptBuilder;
        _builtInTools = builtInTools;
        _logger = logger;

        OnToolInvoking = info =>
        {
            _logger?.Log("Executor", $"[Tool Call]: '{info.Name}' with: {info.Parameters}");
            return Task.FromResult("");
        };

        OnFinalResponseReady = res =>
        {
            _logger?.Log("Executor", $"[Response]: {res}");
        };
    }

    public async Task<string> Execute(string goal)
    {
        var chat = _agentMemory.ChatHistory;
        var tools = _toolRegistry.GetRegisteredTools();

        chat.Add(ChatRole.User, goal);

        var plan = await _planner.GeneratePlan(tools);

        while (plan == null || plan.StepsWithResult.Count == 0)
        {
            chat.Add(ChatRole.User, "You generated no plan.");
            var noPlanFeedback = await _llmClient.GetResponse(chat);
            plan = await _planner.RefinePlan(noPlanFeedback, tools);
        }

        var currentStep = plan.GetCurrentStep();
        OnFinalResponseReady?.Invoke(currentStep);
        return currentStep ?? "No current step.";
    }

}