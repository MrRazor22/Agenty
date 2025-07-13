using Agenty.LLMCore;

namespace Agenty.AgentCore;

public class Agent : IAgent
{
    public string Name { get; }

    private ILLMClient? _llm;
    private IPlanner? _planner;
    private IAgentExecutor? _executor;
    private IAgentMemory? _memory;
    private IToolRegistry? _toolRegistry;
    private IToolExecutor? _toolExecutor;
    private IAgentLogger? _logger;

    public IAgentMemory? Memory => _memory;
    public IPlanner? Planner => _planner;
    public IAgentExecutor? Executor => _executor;


    public Agent(string name)
    {
        Name = name;
    }

    private void EnsureInitialized()
    {
        if (_logger == null)
            _logger = new ConsoleLogger(); // Default logger if not set
        if (_llm == null)
            throw new InvalidOperationException("LLM client is not configured. Use WithModel or WithLLMClient.");
        if (_memory == null) _memory = new AgentMemory(_logger); // Default memory if not set
                                                                 //throw new InvalidOperationException("Set a goal for agent to initialise agent memory");

        if (_toolRegistry == null) _toolRegistry = new ToolRegistry(); // Default tool registry if not set
        if (_toolExecutor == null) _toolExecutor = new ToolExecutor(_toolRegistry); // Default tool executor if not set
        if (_planner == null) _planner = new Planner(_llm, _memory, _logger); // Default planner if not set
        if (_executor == null) _executor = new Executor(_llm, _planner, _memory, _toolExecutor, _toolRegistry, _logger); // Default executor if not set
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
        if (_llm == null) _llm = new OpenAIClient(); // assuming OpenAIClient as default
        _llm.Initialize(baseUrl, apiKey, modelName);
        return this;
    }

    public IAgent WithGoal(string goal)
    {
        if (_memory == null)
            _memory = new AgentMemory(); // Default memory if not set   
        _memory.Goal = goal;
        return this;
    }

    public IAgent WithTool(Delegate func, params string[] tags)
    {
        EnsureToolRegistry().Register(func, tags);
        return this;
    }

    public IAgent WithTools(List<Delegate> tools)
    {
        var registry = EnsureToolRegistry();
        foreach (var t in tools)
            registry.Register(t);
        return this;
    }
    private IToolRegistry EnsureToolRegistry()
    {
        if (_toolRegistry == null)
            _toolRegistry = new ToolRegistry(); // Default tool registry if not set
        return _toolRegistry;
    }

    public IAgent WithMemory(IAgentMemory memory)
    {
        _memory = memory;
        return this;
    }

    public IAgent WithExecutor(IAgentExecutor executor)
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
    #endregion
}
