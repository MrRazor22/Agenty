using Agenty.LLMCore;
using Agenty.LLMCore.OpenAI;

namespace Agenty.AgentCore;

public class Agent : IAgent
{
    public string Name { get; }

    private ILLMClient? _llm;
    private IPlanner? _planner;
    private IExecutor? _executor;
    private IAgentMemory? _memory;
    private ITools? _toolRegistry;
    private IAgentLogger? _logger;
    private ITools? _builtInTools;
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
        _toolRegistry ??= new ToolManager();

        // Register default or overridden agent tools
        _builtInTools ??= new ToolManager();

        //_promptBuilder ??= new StandardPromptBuilder();
        _planner ??= new Planner(_llm, _memory, _toolRegistry, _builtInTools, _logger);
        _executor ??= new Executor(_llm, _planner, _memory, _toolRegistry, _builtInTools, _logger);
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
        _llm ??= new OpenAILLMClient();
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
        EnsureToolRegistry().Register(func);
        return this;
    }

    public IAgent WithTools(List<Delegate> tools)
    {
        var registry = EnsureToolRegistry();
        registry.Register(tools.ToArray());
        return this;
    }

    public IAgent WithAgentTools(Type agentToolsType)
    {
        _agentToolType = agentToolsType;
        return this;
    }

    private ITools EnsureToolRegistry()
    {
        _toolRegistry ??= new ToolManager();
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

    public IAgent WithToolRegistry(ITools toolRegistry)
    {
        _toolRegistry = toolRegistry;
        return this;
    }

    //public IAgent WithPromptBuilder(PromptBuilder promptBuilder)
    //{
    //    _promptBuilder = promptBuilder;
    //    return this;
    //}

    #endregion
}
