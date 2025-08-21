using Agenty.LLMCore;
using Agenty.LLMCore.Providers.OpenAI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.ConstrainedExecution;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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

        public async Task<string> ExecuteAsync(string goal, int maxRounds = 10)
        {
            Console.WriteLine($"[START] Executing goal: {goal}");

            var chat = new Conversation();
            chat.Add(Role.System,
                $@"You operate by running a loop with the following steps: Thought, Action, Observation.
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

                Example session:

                <question>What's the current temperature in Madrid?</question>
                <thought>I need to get the current weather in Madrid</thought>
                <tool_call> {{ ""function"", ""id"": ""0"",{{""function"": {{""name"": ""FetchWeather"",""arguments"": {{""location"": ""Madrid"", ""unit"": ""celsius""}}}}</tool_call>

                You will be called again with this:

                <observation>{{0: {{""temperature"": 25, ""unit"": ""celsius""}}}}</observation>

                You then output:

                <response>The current temperature in Madrid is 25 degrees Celsius</response>

                Additional constraints:

                - If the user asks you something unrelated to any of the tools above, answer freely enclosing your answer with <response></response> tags.
                ");

            chat.Add(Role.User, WrapTag(goal, "question"));

            for (int round = 0; round < maxRounds; round++)
            {
                // 1. Get LLM completion (thought, tool_call, or response)
                var completion = await _llm.GetResponse(chat);

                // 2. Try to extract <response>
                var response = ExtractTagContent(completion, "response");
                if (!string.IsNullOrWhiteSpace(response))
                {
                    return response;
                }

                // 3. Extract <thought> and <tool_call>
                var thought = ExtractTagContent(completion, "thought");
                if (!string.IsNullOrWhiteSpace(thought))
                {
                    Console.WriteLine($"[THOUGHT] {thought}");
                }

                var observations = new Dictionary<string, object>();
                var toolCall = await _toolCoordinator.GetToolCall(chat);
                string result = "No valid response from tool call information provided";
                // Log assistant message
                if (!string.IsNullOrWhiteSpace(toolCall.AssistantMessage))
                {
                    result = toolCall.AssistantMessage;
                }

                // Invoke tool if a tool name exists
                if (!string.IsNullOrWhiteSpace(toolCall.Name))
                {
                    try
                    {
                        // Use T here instead of object
                        result = await _toolCoordinator.Invoke<string>(toolCall);
                        //Console.WriteLine($"[TOOL RESULT] {result}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ERROR] Tool invocation failed: {ex}");
                        result = $"Tool call failed - {ex.Message}";
                        //chat.Add(Role.Assistant, $"Tool invocation failed - {ex}");
                    }

                    //chat.Add(Role.Tool, result?.ToString(), toolCall);
                }
                observations[toolCall.Id] = result;
                Console.WriteLine($"[OBSERVATION] {result}");
                chat.Add(Role.User, WrapTag(JsonSerializer.Serialize(observations), "observation"));

                //var toolCallJsons = ExtractAllTagContents(completion, "tool_call");
                //if (toolCallJsons.Count > 0)
                //{
                //    var observations = new Dictionary<string, object>();
                //    foreach (var toolCallJson in toolCallJsons)
                //    {
                //        var toolCall = ToolCall.FromJson(toolCallJson);
                //        var tool = _toolRegistry.Get(toolCall.Name);
                //        if (tool == null)
                //        {
                //            Console.WriteLine($"[ERROR] Tool not found: {toolCall.Name}");
                //            continue;
                //        }

                //        // Validate and execute tool call
                //        var result = await _toolCoordinator.ExecuteToolCall(chat, tool);
                //        observations[toolCall.Id] = result;
                //        Console.WriteLine($"[TOOL RESULT] {toolCall.Name} => {result}");
                //    }

                // 4. Add observation to chat
                //chat.Add(Role.User, WrapTag(JsonSerializer.Serialize(observations), "observation"));
                //}
            }

            // Fallback: return last LLM response
            return await _llm.GetResponse(chat);
        }

        // Helper methods for tag extraction (implement as needed)
        private string ExtractTagContent(string text, string tag)
        {
            var match = Regex.Match(text, $"<{tag}>([\\s\\S]*?)</{tag}>", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.Trim() : null;
        }

        private List<string> ExtractAllTagContents(string text, string tag)
        {
            var matches = Regex.Matches(text, $"<{tag}>([\\s\\S]*?)</{tag}>", RegexOptions.IgnoreCase);
            return matches.Select(m => m.Groups[1].Value.Trim()).ToList();
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
