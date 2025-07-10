using Agenty.LLMCore;

namespace Agenty.AgentCore
{
    public interface IAgent
    {
        string Name { get; }
        ILLMClient LLMClient { get; }
        IToolRegistry ToolRegistry { get; }
        IToolExecutor ToolExecutor { get; }
        IAgentMemory AgentMemory { get; }
        IPlan? CurrentPlan { get; }
        Task<string> Execute(string userInput);

        // Fluent-style setup
        IAgent WithModel(string baseUrl, string apiKey, string modelName = "any_model"); // e.g. "gpt-4"
        IAgent WithGoal(string goal);
        IAgent WithTools(List<Delegate> tools);
        IAgent WithMemory(IAgentMemory memory);
        IAgent WithExecutor(IAgentExecutor executor);
        IAgent WithPlan(IPlan? plan);

        //hooks
        Func<ToolCallInfo, string>? OnBeforeToolInvoke { get; set; }
        Action<string>? OnThoughtGenerated { get; set; }       // chunk or full "thought"
        Action<string>? OnFinalResponseReady { get; set; }     // final reply only
    }

    public interface IAgentMemory
    {
        IPrompt ChatHistory { get; }
        IScratchpad Scratchpad { get; }
        void Clear();
    }

    public interface IScratchpad
    {
        IReadOnlyList<string> Entries { get; }
        void Add(string entry);
        void ReplaceLast(string entry);
        bool RemoveLast();
        void Clear();
    }
    public interface IPlanner
    {
        Task<IPlan?> GeneratePlan(IAgentMemory memory, string goal);
        Task<IPlan?> RefinePlan(IAgentMemory memory, IPlan existingPlan, string feedback);
    }
    public interface IPlan
    {
        IReadOnlyList<PlanStep> Steps { get; }
        void AddStep(string description);
        public int IndexOf(PlanStep step);
        void MarkComplete(int index, string Insight);
        void Clear();
    }
    public class PlanStep
    {
        public required string Description { get; set; }
        public string? Insight { get; set; } // model’s understanding or result
        public bool IsCompleted { get; set; }
    }
    public interface IAgentExecutor
    {
        Task<string> Execute(IAgent agent, string input);
    }

}
