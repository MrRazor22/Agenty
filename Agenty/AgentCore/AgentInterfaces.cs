using Agenty.LLMCore;
using System;
using System.ComponentModel;

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
        public IExecutor? Executor { get; }
        Task<string> Execute(string userInput);

        // Fluent-style setup
        IAgent WithLLMClient(ILLMClient llmClient);
        IAgent WithModel(string baseUrl, string apiKey, string modelName = "any_model"); // e.g. "gpt-4"
        IAgent WithGoal(string goal);
        IAgent WithTool(Delegate func, params string[] tags);
        IAgent WithTools(List<Delegate> tools);
        Agent WithLogger(IAgentLogger logger); // Optional logger for structured logging
        IAgent WithMemory(IAgentMemory memory);
        IAgent WithExecutor(IExecutor executor);
        IAgent WithPlanner(IPlanner? planner);
        IAgent WithToolRegistry(IToolManager toolRegistry);
        //IAgent WithPromptBuilder(PromptBuilder promptBuilder);
        IAgent WithAgentTools(Type agentToolsType);
    }

    public interface IAgentMemory//takes it goal optionally
    {
        Action<string>? OnThoughtGenerated { get; set; }       // both streaming or full "thought" 
        Action<string>? OnChatHistoryUpdated { get; set; }
        string Goal { get; set; }
        IPlan Plan { get; set; }
        IScratchpad Thoughts { get; }
        ChatHistory ChatHistory { get; }
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
        //Action<IPlan>? OnPlanUpdated { get; set; }
        //Task<IPlan?> GeneratePlan(List<Tool> availableTools = null);
        //Task<IPlan?> RefinePlan(string feedback, List<Tool>? availableTools = null);
    }
    public interface IPlan
    {
        void AddStep(string step);
        string? GetCurrentStep();
        void SetResultForCurrentStep(StepResult result);
        int CurrentStepIndex { get; }
        IReadOnlyList<(string step, StepResult result)> StepsWithResult { get; }
    }

    public class Plan : IPlan
    {
        public List<(string step, StepResult result)> StepsWithResult { get; set; } = new();
        public int CurrentStepIndex { get; private set; } = 0;

        IReadOnlyList<(string step, StepResult result)> IPlan.StepsWithResult => StepsWithResult;

        public void AddStep(string step)
        {
            StepsWithResult.Add((step, new StepResult()));
        }

        public string? GetCurrentStep()
        {
            if (CurrentStepIndex < 0 || CurrentStepIndex >= StepsWithResult.Count)
                return null;
            return StepsWithResult[CurrentStepIndex].step;
        }

        public void SetResultForCurrentStep(StepResult result)
        {
            if (CurrentStepIndex < 0 || CurrentStepIndex >= StepsWithResult.Count)
                return;
            StepsWithResult[CurrentStepIndex] = (StepsWithResult[CurrentStepIndex].step, result);
            CurrentStepIndex++;
        }
        public override string ToString()
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < StepsWithResult.Count; i++)
            {
                var (step, result) = StepsWithResult[i];
                sb.AppendLine($"{i + 1}. {step}");

                if (result != null && (result.Outcome != default || !string.IsNullOrWhiteSpace(result.Message)))
                {
                    sb.AppendLine($"   → Outcome: {result.Outcome}");
                    if (!string.IsNullOrWhiteSpace(result.Message))
                        sb.AppendLine($"   → Note: {result.Message}");
                }
            }

            return sb.ToString();
        }

    }

    public enum Outcome
    {
        [Description("The step was Not executed yet")]
        None = 0,
        [Description("The step was successfully completed.")]
        Completed,

        [Description("The step failed.")]
        Failed,

        [Description("The step was intentionally skipped.")]
        Skipped,

        [Description("Retry the step again.")]
        Retry,

        [Description("The plan needs to be adjusted.")]
        Replan
    }

    public class StepResult
    {
        [Description("The outcome of the step.")]
        public Outcome Outcome { get; set; } = 0;

        [Description("Optional message with insight, failure reason, or notes.")]
        public string? Message { get; set; }
    }


    public interface IExecutor//takes in IPlanner, ILLMClient, IToolExecutor, IAgentMemory
    {
        Func<Tool, Task<string>>? OnToolInvoking { get; set; }
        Action<string>? OnFinalResponseReady { get; set; }
        Task<string> Execute(string input);
    }
}
