using Agenty.LLMCore;

namespace Agenty.AgentCore;

public class Agent : IAgent
{
    public string Name { get; }

    private ILLMClient? _llm;
    private IPlanner? _planner;
    private IExecutor? _executor;
    private IAgentMemory? _memory;
    private IToolRegistry? _toolRegistry;
    private IToolExecutor? _toolExecutor;
    private PromptBuilder? _promptBuilder;
    private IAgentLogger? _logger;
    private BuiltInTools? _builtInTools;
    private Type _agentToolType = typeof(AgentTools);

    public IAgentMemory? Memory => _memory;
    public IPlanner? Planner => _planner;
    public IExecutor? Executor => _executor;

    public Agent(string name)
    {
        Name = name;
    }

    private void EnsureInitialized()
    {
        if (_llm == null)
            throw new InvalidOperationException("LLM client is not configured. Use WithModel or WithLLMClient.");

        _logger ??= new ConsoleLogger();
        _memory ??= new AgentMemory(_logger);
        _toolRegistry ??= new ToolRegistry();

        // Register default or overridden agent tools
        _toolRegistry.RegisterAllFromType(_agentToolType);
        _builtInTools ??= new BuiltInTools(_toolRegistry, _agentToolType);

        _promptBuilder ??= new StandardPromptBuilder();
        _toolExecutor ??= new ToolExecutor(_toolRegistry);
        _planner ??= new Planner(_llm, _memory, _toolExecutor, _toolRegistry, _promptBuilder, _builtInTools, _logger);
        _executor ??= new Executor(_llm, _planner, _memory, _toolExecutor, _toolRegistry, _promptBuilder, _builtInTools, _logger);
    }

    public Task<string> Execute(string userInput)
    {
        EnsureInitialized();
        return _executor!.Execute(userInput);
    }

    #region fluentAPI

    public Agent WithLogger(IAgentLogger logger)
    {
        _logger = logger;
        return this;
    }

    public IAgent WithLLMClient(ILLMClient llmClient)
    {
        _llm = llmClient;
        return this;
    }

    public IAgent WithModel(string baseUrl, string apiKey, string modelName = "any_model")
    {
        _llm ??= new OpenAIClient();
        _llm.Initialize(baseUrl, apiKey, modelName);
        return this;
    }

    public IAgent WithGoal(string goal)
    {
        _memory ??= new AgentMemory();
        _memory.Goal = goal;
        return this;
    }

    public IAgent WithTool(Delegate func, params string[] tags)
    {
        EnsureToolRegistry().Register(func, tags: tags);
        return this;
    }

    public IAgent WithTools(List<Delegate> tools)
    {
        var registry = EnsureToolRegistry();
        registry.RegisterAll(tools);
        return this;
    }

    public IAgent WithAgentTools(Type agentToolsType)
    {
        _agentToolType = agentToolsType;
        return this;
    }

    private IToolRegistry EnsureToolRegistry()
    {
        _toolRegistry ??= new ToolRegistry();
        return _toolRegistry;
    }

    public IAgent WithMemory(IAgentMemory memory)
    {
        _memory = memory;
        return this;
    }

    public IAgent WithExecutor(IExecutor executor)
    {
        _executor = executor;
        return this;
    }

    public IAgent WithPlanner(IPlanner? planner)
    {
        _planner = planner;
        return this;
    }

    public IAgent WithToolRegistry(IToolRegistry toolRegistry)
    {
        _toolRegistry = toolRegistry;
        return this;
    }

    public IAgent WithToolExecutor(IToolExecutor toolExecutor)
    {
        _toolExecutor = toolExecutor;
        return this;
    }

    public IAgent WithPromptBuilder(PromptBuilder promptBuilder)
    {
        _promptBuilder = promptBuilder;
        return this;
    }

    #endregion
}
