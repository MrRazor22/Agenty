using Agenty.LLMCore;
using Agenty.LLMCore.Providers.OpenAI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.ConstrainedExecution;
using System.Text;
using System.Threading.Tasks;
using Xunit.Sdk;

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
            Console.WriteLine($"[START] Executing goal: {goal}");

            var chat = new Conversation();
            chat.Add(Role.System,
    "You are a careful, step-by-step assistant. " +
    "For complex queries, plan actions one at a time. " +
    "If unsure, say 'I don't know'. " +
    $"Use the listed tools ({_toolRegistry}) ONLY if needed. " +
    "Keep your responses short, clear, and actionable.");

            chat.Add(Role.User, goal);

            bool IsDone = false;
            string final = "";

            string reflection = null;

            while (IsDone == false)
            {
                PlanStep nxt = null;
                while (string.IsNullOrWhiteSpace(nxt?.NextStep))
                {
                    if (reflection == null)
                    {
                        var nxtChat = Conversation.Clone(chat);
                        nxtChat.Add(Role.User,
    "Decide the next single action to move toward the goal. " +
    "If a tool is needed, specify its exact name from the list of tools. " +
    "Do NOT include irrelevant actions. " +
    "Respond with only two fields: NextStep and Tool.");

                        nxt = await _toolCoordinator.GetStructuredResponse<PlanStep>(nxtChat);

                        if (string.IsNullOrWhiteSpace(nxt.NextStep))
                        {
                            Console.WriteLine("[PLANNER] Got empty reflection, retrying...");
                            continue;
                        }

                        Console.WriteLine($"[PLANNER RESULT] {nxt.NextStep} | {nxt.Tool}");
                        chat.Add(Role.User, $"Perform the action: {nxt.NextStep} with the tool {nxt.Tool}");
                    }
                    else
                    {
                        var nxtChat = Conversation.Clone(chat);
                        nxtChat.Add(Role.User, $"I think your previous answer direction was wrong because i think you should {reflection}");
                        nxtChat.Add(Role.User,
                            "Give me what next simple one action you want to do next to move forward in accomplising my query, and also let me know thw tool to be used for that from list of tools you have access to.");

                        nxt = await _toolCoordinator.GetStructuredResponse<PlanStep>(nxtChat);

                        if (nxt != null || string.IsNullOrWhiteSpace(nxt.NextStep) || (string.IsNullOrEmpty(nxt.Tool) && _toolRegistry.Get(nxt?.Tool) != null))
                        {
                            Console.WriteLine("[PLANNER] Got empty reflection, retrying...");
                            continue;
                        }

                        Console.WriteLine($"[PLANNER RESULT] {nxt.NextStep} | {nxt.Tool}");
                        chat.Add(Role.User, $"Perform the action: {nxt.NextStep} with the tool {nxt.Tool}");
                    }
                }

                Console.WriteLine("[LOOP] Getting tool call...");
                ToolCall toolCall = null;
                if (_toolRegistry.Get(nxt?.Tool) == null)
                    await _toolCoordinator.ExecuteToolCall(chat, _toolRegistry.RegisteredTools.ToArray());
                else await _toolCoordinator.ExecuteToolCall(chat, _toolRegistry.Get(nxt?.Tool));


                Console.WriteLine("[VALIDATION] Checking if goal is satisfied...");
                var validationChat = Conversation.Clone(chat);
                validationChat.Add(Role.Assistant,
    $"Does your response fully satisfy the user's goal: \"{goal}\"? " +
    "Answer with only 'true' or 'false'.");

                IsDone = await _toolCoordinator.GetStructuredResponse<bool>(validationChat);
                Console.WriteLine($"[VALIDATION RESULT] IsDone = {IsDone}");

                if (IsDone)
                {
                    while (string.IsNullOrWhiteSpace(final))
                    {
                        Console.WriteLine("[FINAL] Attempting to generate final response...");
                        var finalChat = Conversation.Clone(chat);

                        finalChat.Add(Role.User,
    $"Provide a concise final answer to the goal: \"{goal}\". " +
    "Use 2–3 sentences. " +
    "Be honest and clear. Do not leave blank.");

                        final = await _llm.GetResponse(finalChat);

                        if (string.IsNullOrEmpty(final))
                        {
                            Console.WriteLine("[FINAL] Got empty final response, retrying...");
                            continue;
                        }

                        Console.WriteLine($"[FINAL RESULT] =================================================================>");
                    }

                }

                if (!IsDone || string.IsNullOrEmpty(final))
                {
                    Console.WriteLine("[REFLECTION] Generating reflection...");
                    while (string.IsNullOrWhiteSpace(reflection))
                    {
                        var reflectChat = Conversation.Clone(chat);
                        reflectChat.Add(Role.User,
                            "Reflect on progress toward the goal. " +
                            "Answer in one short sentence only. " +
                            "Do not leave blank. Be beutally honest.");

                        reflection = await _llm.GetResponse(reflectChat);

                        if (string.IsNullOrWhiteSpace(reflection))
                        {
                            Console.WriteLine("[REFLECTION] Got empty reflection, retrying...");
                            continue;
                        }

                        Console.WriteLine($"[REFLECTION RESULT] {reflection}");
                        chat.Add(Role.Assistant, reflection);
                    }
                }
            }

            return final;
        }

    }
}
