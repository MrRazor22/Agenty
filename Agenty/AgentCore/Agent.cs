using Agenty.LLMCore;
using Agenty.LLMCore.Providers.OpenAI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.ConstrainedExecution;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit.Sdk;

namespace Agenty.AgentCore
{
    public class ReActAgent : IAgent
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

        private ReActAgent() { }
        public static ReActAgent Create() => new ReActAgent();
        public ReActAgent WithLLM(string baseUrl, string apiKey, string modelName = "any_model")
        {
            _llm = new OpenAILLMClient();
            _llm.Initialize(baseUrl, apiKey, modelName);
            _toolCoordinator = new ToolCoordinator(_llm, _toolRegistry);
            return this;
        }

        public ReActAgent WithTools<T>()
        {
            _toolRegistry.RegisterAll<T>();
            return this;
        }

        public ReActAgent WithTools(params Delegate[] tools)
        {
            _toolRegistry.Register(tools);
            return this;
        }

        public ReActAgent WithComponents(int critiqueInterval = 3)
        {
            this.critiqueInterval = critiqueInterval;
            return this;
        }

        public async Task<string> ExecuteAsync(string goal, int maxRounds = 100)
        {
            Console.WriteLine($"[START] Executing goal: {goal}");

            var chat = InitializeConversation(goal);

            for (int round = 0; round < maxRounds; round++)
            {
                var completion = await _llm.GetResponse(chat);

                // Handle <thought>
                if (TryExtractAllTags(completion, "thought", out string thoughts))
                {
                    chat.Add(Role.Assistant, WrapTag(thoughts, "thought"));
                    Console.WriteLine($"[THOUGHT] {thoughts}");
                }

                // Handle <response>
                if (TryExtractAllTags(completion, "response", out string responses))
                {
                    chat.Add(Role.Assistant, WrapTag(responses, "response"));
                    return responses;
                }

                if (completion.Contains("<tool_call>") && !completion.Contains("</tool_call>"))
                    completion += "</tool_call>";

                if (TryExtractAllTags(completion, "tool_call", out List<string> toolCalls))
                {
                    foreach (var toolCallJson in toolCalls)
                    {
                        try
                        {
                            var toolCall = _toolCoordinator.TryExtractInlineToolCall(toolCallJson);

                            if (toolCall == null)
                            {
                                Console.WriteLine("[WARN] Failed to parse tool call JSON.");
                                continue;
                            }

                            // Log + add to chat
                            chat.Add(Role.Assistant, WrapTag(toolCallJson, "tool_call"), toolCall);

                            // ✅ Execute tool call using coordinator
                            var observation = await HandleToolCall(toolCall);

                            // Push observation back into chat
                            chat.Add(Role.User, WrapTag(JsonSerializer.Serialize(
                                new Dictionary<string, object> { [toolCall.Id] = observation }
                            ), "observation"));

                            Console.WriteLine($"[OBSERVATION] {observation}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[ERROR] Tool call processing failed: {ex}");
                            chat.Add(Role.User, WrapTag($"Tool call processing failed: {ex.Message}", "observation"));
                        }
                    }
                }
            }
            return await _llm.GetResponse(chat);
        }

        private string GenerateSystemPrompt()
        {
            return $@"You operate by running a loop with the following steps: Thought, Action, Observation.
                You are provided with function signatures within <tools></tools> XML tags.
                You may call one or more functions to assist with the user query. Don't make assumptions about what values to plug
                into functions. Pay special attention to the properties 'types'. You should use those types as in a Python dict.

                For each function call return a json object with function name and arguments within <tool_call></tool_call> XML tags as follows:

                <tool_call>
                {{""name"": ""<function-name>"", ""arguments"": <args-dict>, ""id"": <monotonically-increasing-id>}}
                </tool_call>

                Here are the available tools / actions:

                <tools>
                {_toolRegistry}
                </tools>
                
                Follow this format in your response:

                Example session (tool use needed):
                <question>What's the current temperature in Madrid?</question>
                <thought>I need to get the current weather in Madrid</thought>
                <tool_call> {{ ""function"", ""id"": ""0"",{{""function"": {{""name"": ""FetchWeather"",""arguments"": {{""location"": ""Madrid"", ""unit"": ""celsius""}}}}</tool_call>

                You will be called again with this:

                <observation>{{0: {{""temperature"": 25, ""unit"": ""celsius""}}}}</observation>

                You then output:

                <response>The current temperature in Madrid is 25 degrees Celsius</response>
                
                ---

                Example session (tool use **not** needed):
                <question>Who is the capital of Japan?</question>         
                <thought>This is general knowledge. No tool call is needed</thought>
                <response>The capital of Japan is Tokyo.</response>

                ---

                Guidelines:
                - ""Always output in one of these formats:

                <thought> ... </thought>

                <response> ... </response>

                <tool_call>{{json}}</tool_call>
                Do not use sny other markers.""
                - Use '<tool_call>' only when external data or computation is required.
                - If the answer is known or can be reasoned directly, skip tool use and respond immediately.
                - Always include a '<thought>' explaining your reasoning before deciding to use a tool or not.
                - Final answers must be enclosed strictly within <response> tags, with no additional text outside the tags.
                - Do not re-call the same tool unless the observation indicates an error or missing data.
                - Always base reasoning strictly on the latest <observation>.
                - Never re-invent or reinterpret values from tools.
                - Treat tool outputs as ground truth and use them directly in reasoning.
                - Valid tags are only: <thought>, <tool_call>, <observation>, <response>.
                ";
        }

        private Conversation InitializeConversation(string goal)
        {
            var chat = new Conversation();
            chat.Add(Role.System, GenerateSystemPrompt());
            chat.Add(Role.User, WrapTag(goal, "question"));
            return chat;
        }

        private async Task<string> HandleToolCall(ToolCall toolCall)
        {
            if (string.IsNullOrEmpty(toolCall.Name) && string.IsNullOrEmpty(toolCall.AssistantMessage))
            {
                return "No valid tool call";
            }
            else if (!string.IsNullOrWhiteSpace(toolCall.AssistantMessage))
            {
                return Regex.Replace(toolCall.AssistantMessage, @"<\/?(thought|response)[^>]*>", "");
            }

            try
            {
                return await _toolCoordinator.Invoke<string>(toolCall);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Tool invocation failed: {ex}");
                return $"Tool call failed - {ex.Message}";
            }
        }

        private bool TryExtractAllTags(string text, string tag, out string joinedContent)
        {
            var matches = Regex.Matches(text, $"<{tag}>([\\s\\S]*?)</{tag}>", RegexOptions.IgnoreCase);

            if (matches.Count > 0)
            {
                joinedContent = string.Join(" ", matches.Cast<Match>().Select(m => m.Groups[1].Value.Trim()));
                return true;
            }

            joinedContent = null!;
            return false;
        }
        private bool TryExtractAllTags(string text, string tag, out List<string> content)
        {
            var matches = Regex.Matches(text, $"<{tag}>([\\s\\S]*?)</{tag}>", RegexOptions.IgnoreCase);

            if (matches.Count > 0)
            {
                content = matches.Cast<Match>().Select(m => m.Groups[1].Value.Trim()).ToList();
                return true;
            }

            content = null!;
            return false;
        }

        public string WrapTag(string content, string tag = "")
        {
            // Wrap with tag if provided
            if (!string.IsNullOrWhiteSpace(tag))
            {
                content = $"<{tag}>{content}</{tag}>";
            }
            return content;
        }

    }
}
