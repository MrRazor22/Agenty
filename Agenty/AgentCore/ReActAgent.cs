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
        private ILLMClient _llm;
        private IToolCoordinator _toolCoordinator;
        private IToolRegistry _toolRegistry = new ToolRegistry();
        Conversation chat;

        public static ReActAgent Create() => new ReActAgent();
        public ReActAgent WithLLM(string baseUrl, string apiKey, string modelName)
        {
            _llm = new OpenAILLMClient();
            _llm.Initialize(baseUrl, apiKey, modelName);
            _toolCoordinator = new ToolCoordinator(_llm, _toolRegistry);
            return this;
        }

        public ReActAgent WithTools<T>()
        {
            _toolRegistry.RegisterAll<T>();

            chat = new Conversation().Add(Role.System, GenerateSystemPrompt());
            // Console.WriteLine("Sys prompt: " + GenerateSystemPrompt());
            return this;
        }

        public async Task<string> ExecuteAsync(string goal, int maxRounds = 50)
        {
            Console.WriteLine($"[START] Executing goal: {goal}");

            chat.Add(Role.User, goal);
            int consecutiveFailures = 0;

            for (int round = 0; round < maxRounds; round++)
            {
                //Console.WriteLine($"[ROUND {round + 1}] Starting reasoning cycle...");

                try
                {
                    var completion = await _llm.GetResponse(chat);
                    chat.Add(Role.Assistant, completion, isTemporary: true);
                    #region Guardrails
                    if (completion.Contains("<response>") && completion.Contains("<tool_call>"))
                    {
                        Console.WriteLine("[RETRYING] You must not output <response> in the same turn as a <tool_call>. After the tool_call and wait for system to provide <observation>.");
                        chat.Add(Role.System, "You must not output <response> in the same turn as a <tool_call>. After the tool_call and wait for system to provide <observation>.", isTemporary: true);
                        continue;
                    }

                    if (completion.Contains("<observation>"))
                    {
                        Console.WriteLine("[RETRYING] You must never output <observation>. Only the system provides <observation> after a <tool_call>.");
                        chat.Add(Role.System, "You must never output <observation>. Only the System provides <observation> after a <tool_call>.", isTemporary: true);
                        continue;
                    }

                    // If no valid action was found, give simple feedback
                    if (!completion.Contains("<thought>") && !completion.Contains("<response>") && !completion.Contains("<tool_call>"))
                    {
                        Console.WriteLine("[RETRYING] Please use <thought>, <tool_call>, or <response> tags.");
                        chat.Add(Role.System, WrapTag("Please use <thought>, <tool_call>, or <response> tags.", "observation"), isTemporary: true);
                        continue;
                    }

                    if (completion.Contains("<tool_call>") && !completion.Contains("</tool_call>"))
                    {
                        Console.WriteLine("[RETRYING] Invalid tool call. You must return a complete JSON object wrapped in <tool_call>...</tool_call>.");
                        chat.Add(
                            Role.System,
                            "Invalid tool call. Respond only with:\n<tool_call>{ \"name\": \"ToolName\", \"arguments\": { ... } }</tool_call>",
                            isTemporary: true
                        );
                    }

                    // Stop if we get too many empty responses in a row
                    if (string.IsNullOrWhiteSpace(completion))
                    {
                        consecutiveFailures++;
                        if (consecutiveFailures >= 3)
                        {
                            return "I'm having trouble responding. Please try rephrasing your request.";
                        }
                    }
                    else
                    {
                        consecutiveFailures = 0;
                    }
                    #endregion

                    //Console.WriteLine($"[LLM_RESPONSE] {completion}");

                    // Handle <thought>
                    if (TryExtractAllTags(completion, "thought", out string thoughts))
                    {
                        chat.Add(Role.Assistant, WrapTag(thoughts, "thought"));
                        Console.WriteLine($"[THOUGHT] {thoughts}");
                    }

                    // Handle <response> - Final answer
                    if (TryExtractAllTags(completion, "response", out string responses))
                    {
                        chat.Add(Role.Assistant, WrapTag(responses, "response"));
                        Console.WriteLine($"[FINAL_RESPONSE] {responses}");
                        return responses;
                    }

                    // Handle tool calls - allow multiple in one response
                    if (completion.Contains("<tool_call>"))
                    {
                        if (TryExtractAllTags(completion, "tool_call", out List<string> toolCalls))
                        {
                            bool anyToolExecuted = false;

                            foreach (var toolCallJson in toolCalls)
                            {
                                try
                                {
                                    //Console.WriteLine($"[TOOL_CALL_RAW] {toolCallJson}");

                                    var toolCall = _toolCoordinator.TryExtractInlineToolCall(toolCallJson);

                                    if (toolCall == null)
                                    {
                                        Console.WriteLine("[RETRYING] Tool call parsing failed. Check your JSON format.");
                                        chat.Add(Role.User, WrapTag("Tool call parsing failed. Check your JSON format.", "observation"), isTemporary: true);
                                        continue;
                                    }

                                    // Add to chat
                                    chat.Add(Role.Assistant, WrapTag(toolCallJson, "tool_call"), toolCall);

                                    // Execute tool call
                                    var observation = await _toolCoordinator.HandleToolCall(toolCall);
                                    observation = Regex.Replace(observation, @"<\/?(thought|response)[^>]*>", "");

                                    // Add observation
                                    var observationData = new Dictionary<string, object> { [toolCall.Id] = observation };
                                    chat.Add(Role.System, WrapTag(JsonSerializer.Serialize(observationData), "observation"));

                                    Console.WriteLine($"[OBSERVATION] {observation}");
                                    anyToolExecuted = true;
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"[RETRYING] Tool '{{toolCallJson}}' failed: {{ex.Message}}. Continue without this data.");
                                    chat.Add(Role.System, WrapTag($"Tool '{toolCallJson}' failed: {ex.Message}. Continue without this data.", "observation"), isTemporary: true);
                                }
                            }

                            if (anyToolExecuted)
                            {
                                continue; // Continue to next round to process observations
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[RETRYING] An error occurred: {ex.Message}. Please continue.");
                    chat.Add(Role.System, WrapTag($"An error occurred: {ex.Message}. Please continue.", "observation"), isTemporary: true);
                }
            }

            return "Maximum rounds reached without completing the task.";
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
                {_toolRegistry.ToString()}
                </tools>
                
                Follow this format in your response:

                Example session (tool use needed):
                <question>What's the current temperature in Madrid?</question>
                <thought>I need to get the current weather in Madrid</thought>
                <tool_call> {{""name"": ""FetchWeather"",""arguments"": {{""location"": ""Madrid"", ""unit"": ""celsius""}}, ""id"": ""0""}}</tool_call>

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
                - If you output a <tool_call> tag, you must **wait for an <observation> response from the system**. 
                - Never invent or hallucinate <observation> yourself.
                - If the system replies `[ERROR] Tool call Incomplete`, you must immediately retry outputting a corrected <tool_call>.
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
            if (text.Contains("<tool_call>") && !text.Contains("</tool_call>"))
            {
                text += "</tool_call>";
            }
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
            if (!string.IsNullOrWhiteSpace(tag)) content = $"<{tag}>{content}</{tag}>";
            return content;
        }
    }
}