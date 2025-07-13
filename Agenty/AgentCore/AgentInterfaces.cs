using Agenty.LLMCore;
using System;

namespace Agenty.AgentCore
{
    public interface IAgentLogger
    {
        void Log(string source, string message);
    }
    public class ConsoleLogger : IAgentLogger
    {
        public void Log(string source, string message) => Console.WriteLine($"[{source}] {message}");
    }
    public interface IAgent
    {
        string Name { get; }// Name of the agent, can be used for identification
        public IAgentMemory? Memory { get; }
        public IPlanner? Planner { get; }
        public IAgentExecutor? Executor { get; }
        Task<string> Execute(string userInput);

        // Fluent-style setup
        IAgent WithLLMClient(ILLMClient llmClient);
        IAgent WithModel(string baseUrl, string apiKey, string modelName = "any_model"); // e.g. "gpt-4"
        IAgent WithGoal(string goal);
        IAgent WithTool(Delegate func, params string[] tags);
        IAgent WithTools(List<Delegate> tools);
        Agent WithLogger(IAgentLogger logger); // Optional logger for structured logging
        IAgent WithMemory(IAgentMemory memory);
        IAgent WithExecutor(IAgentExecutor executor);
        IAgent WithPlanner(IPlanner? planner);
        IAgent WithToolRegistry(IToolRegistry toolRegistry);
        IAgent WithToolExecutor(IToolExecutor toolExecutor);
    }

    public interface IAgentMemory//takes it goal optionally
    {
        Action<string>? OnThoughtGenerated { get; set; }       // both streaming or full "thought" 
        Action<string>? OnChatHistoryUpdated { get; set; }
        string Goal { get; set; }
        IPlan Plan { get; set; }
        IScratchpad Thoughts { get; }
        IPrompt ChatHistory { get; }
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
    public interface IPlanner//takes in ILLMClient, IAgentMemory 
    {
        Action<IPlan>? OnPlanUpdated { get; set; }
        Task<IPlan?> GeneratePlan(string goal, List<Tool> availableTools = null);
        Task<IPlan?> RefinePlan(IPlan existingPlan, string feedback, List<Tool> availableTools = null);
    }
    public interface IPlan
    {
        int CurrentStepIndex { get; set; }
        IReadOnlyList<PlanStep> Steps { get; }
        public int IndexOf(PlanStep step);
        void MarkComplete(int index, string Insight);
    }
    public class PlanStep
    {
        public required string Description { get; set; }
        public string? Insight { get; set; } // model’s understanding or result
        public bool IsCompleted { get; set; }
    }
    public interface IAgentExecutor//takes in IPlanner, ILLMClient, IToolExecutor, IAgentMemory
    {
        Func<ToolCallInfo, Task<string>>? OnToolInvoking { get; set; }
        Action<string>? OnFinalResponseReady { get; set; }
        Task<string> Execute(string input);
    }
}
