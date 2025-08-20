using Agenty.LLMCore;
using Agenty.LLMCore.Providers.OpenAI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Agenty.AgentCore
{
    public class Agent : IAgent
    {
        private string _goal = "";
        private readonly Scratchpad _scratchpad = new();
        private FeedBack? _criticFeedback;

        private int stepsSinceLastCritique = 0;
        private int critiqueInterval = 3;
        private int lastGoodStepIndex = 0;

        private ILLMClient _llm;
        private ToolCoordinator _toolCoordinator;
        private IToolRegistry _toolRegistry = new ToolRegistry();

        private Agent() { }
        public static Agent Create() => new Agent();
        public Agent WithLLM(string baseUrl, string apiKey, string modelName = "any_model")
        {
            _llm = new OpenAILLMClient();
            _llm.Initialize(baseUrl, apiKey, modelName);
            _toolCoordinator = new ToolCoordinator(_llm, _toolRegistry);
            return this;
        }

        public Agent WithTools<T>()
        {
            _toolRegistry.RegisterAll<T>();
            return this;
        }

        public Agent WithTools(params Delegate[] tools)
        {
            _toolRegistry.Register(tools);
            return this;
        }

        public Agent WithComponents(int critiqueInterval = 3)
        {
            this.critiqueInterval = critiqueInterval;
            return this;
        }

        public async Task<string> ExecuteAsync(string goal)
        {
            _goal = goal;
            _scratchpad.Clear(); // Clear previous execution history
            var isDone = false;
            int maxSteps = 100; // Add step limit to prevent infinite loops
            int stepCount = 0;

            while (!isDone && stepCount < maxSteps)
            {
                stepCount++;
                string context = _scratchpad.Entries.Any() ?
                    $"Here are the actions taken so far to move towards accomplishing the goal: {_scratchpad}" : "Did Nothing";
                string latestContext = _scratchpad.Entries.Any() ?
                    $"Here is your previous action taken: {_scratchpad.Entries.LastOrDefault().ActionTaken} amd Insights you got: {_scratchpad.Entries.LastOrDefault().Insights}, so plan based on these insights "
                    : "";

                //planner
                //var chat = new Conversation()
                //     .Add(Role.System,
                //         "You are a planning assistant tasked with providing the NEXT actionable step for any user goal." +
                //         $"Here is the list of tools user can access: [{_toolRegistry}]. " +
                //         $"What we did so far for this goal: {context} " +
                //         $"{latestContext}" +
                //         "What one single step to do next? JUST TELL EXACTLY ONE SIMPLE ACTION TO DO NEXT TO MOVE FORWARD FROM CURRENT POSITION")
                //        .Add(Role.User, $"{_goal}");


                //var whatNext = await _llm.GetResponse(chat);
                //Console.WriteLine($"THOUGHTS: {whatNext}");

                PlanStep step = await _toolCoordinator.GetStructuredResponse<PlanStep>(new Conversation()
                    .Add(Role.System,
                     $"Here is the list of tools user can access: [{_toolRegistry}]. " +
                        "Extract each actionable step from the user goal. " +
                        "Each step must name the exact tool and include a specific description tied to the actual goal, " +
                        "not a vague phrase like 'evaluate expression'. " +
                        "Description should explain concretely what is being done in this case.")
                    .Add(Role.User, $"User goal: {_goal}"));
                //+ $"\nPlanner output: {whatNext}"));

                Console.WriteLine("========================================> PLAN GENERATED");
                Console.WriteLine($" {step.Description} | {step.ToolName}\n");








































                //executor
                var executorChat = new Conversation()
    .Add(Role.System, $"You are an execution assistant. Execute the step provided: '{step.Description}' for the original goal: '{_goal}'. " +
                      $"Tools accessible: [{_toolRegistry.Get(step.ToolName)?.ToString() ?? _toolRegistry.ToString()}]. " +
                      "Extract any necessary parameters from the original goal and use the tool with those specific parameters.")
    .Add(Role.User, $"Step: {step.Description}, Original Goal: {_goal}");

                ToolCall toolCall;
                if (_toolRegistry.Get(step.ToolName) != null)
                    toolCall = await _toolCoordinator.GetToolCall(executorChat, tools: _toolRegistry.Get(step.ToolName));
                else
                    toolCall = await _toolCoordinator.GetToolCall(executorChat);

                object? result = "No step execution result";
                if (!string.IsNullOrWhiteSpace(toolCall.AssistantMessage) &&
                    string.IsNullOrWhiteSpace(toolCall.Name))
                {
                    result = toolCall.AssistantMessage;
                }
                if (!string.IsNullOrWhiteSpace(toolCall.Name))
                {
                    try
                    {
                        result = await _toolCoordinator.Invoke<object>(toolCall);
                    }
                    catch (Exception ex)
                    {
                        result = $"Tool invocation failed - {ex}";
                    }
                }
                Console.WriteLine($"RESULT: {result?.ToString()}");

                //feedback 
                var critiqChat = new Conversation()
                    .Add(Role.System, $"You are a feedback assistant who evaluates progress toward accomplishing the goal: '{_goal}' so that planner assistnat can listten and follow your feedback and course correct " +
                        $"{context} " +
                        $"{latestContext}" +
                        $"Your job is to: " +
                        $"1. Determine if the ENTIRE GOAL is now accomplished based on ALL steps taken so far " +
                        $"2. if goal not accomplished, clealry state why it was not accomplished and corrective actions to be taken" +
                        $"IMPORTANT: The goal is accomplished when the random number task is complete. " +
                        $"Do NOT repeat completed tasks. Be concise and accurate.")
                    .Add(Role.User, $"Goal: {_goal}");

                FeedBack feedBack = await _toolCoordinator.GetStructuredResponse<FeedBack>(critiqChat);
                Console.WriteLine($"FEEDBACK: {feedBack.Understanding} | {feedBack.IsGoalAccomplished}");

                // CRITICAL FIX: Actually add the entry to the scratchpad
                var scratchPadEntry = new ScratchpadEntry
                {
                    ActionTaken = step.Description,
                    Insights = feedBack.Understanding
                };
                _scratchpad.AddEntry(scratchPadEntry); // This was missing!

                isDone = feedBack.IsGoalAccomplished;

                if (isDone)
                {
                    return $"Goal accomplished after {stepCount} steps.";
                }
            }

            return $"Execution completed or step limit ({maxSteps}) reached after {stepCount} steps.";
        }
    }
}
