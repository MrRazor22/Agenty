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
            chat = new Conversation().Add(Role.System, GenerateSystemPrompt());
            return this;
        }

        public ReActAgent WithTools<T>()
        {
            _toolRegistry.RegisterAll<T>();
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

                    if (string.IsNullOrWhiteSpace(completion))
                    {
                        Console.WriteLine("[WARN] Empty completion received");
                        continue;
                    }

                    Console.WriteLine($"[LLM_RESPONSE] {completion}");

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
                                        Console.WriteLine("[WARN] Failed to parse tool call JSON");
                                        chat.Add(Role.User, WrapTag("Tool call parsing failed. Check your JSON format.", "observation"));
                                        continue;
                                    }

                                    // Add to chat
                                    chat.Add(Role.Assistant, WrapTag(toolCallJson, "tool_call"), toolCall);

                                    // Execute tool call
                                    var observation = await _toolCoordinator.HandleToolCall(toolCall);
                                    observation = Regex.Replace(observation, @"<\/?(thought|response)[^>]*>", "");

                                    // Add observation
                                    var observationData = new Dictionary<string, object> { [toolCall.Id] = observation };
                                    chat.Add(Role.User, WrapTag(JsonSerializer.Serialize(observationData), "observation"));

                                    Console.WriteLine($"[OBSERVATION] {observation}");
                                    anyToolExecuted = true;
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"[ERROR] Tool call failed: {ex}");
                                    chat.Add(Role.User, WrapTag($"Tool '{toolCallJson}' failed: {ex.Message}. Continue without this data.", "observation"));
                                }
                            }

                            if (anyToolExecuted)
                            {
                                continue; // Continue to next round to process observations
                            }
                        }
                    }

                    // If no valid action was found, give simple feedback
                    if (!completion.Contains("<thought>") && !completion.Contains("<response>") && !completion.Contains("<tool_call>"))
                    {
                        Console.WriteLine("[WARN] No valid tags found");
                        chat.Add(Role.User, WrapTag("Please use <thought>, <tool_call>, or <response> tags.", "observation"));
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
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Round failed: {ex}");
                    chat.Add(Role.User, WrapTag($"An error occurred: {ex.Message}. Please continue.", "observation"));
                }
            }

            return "Maximum rounds reached without completing the task.";
        }

        private string GenerateSystemPrompt()
        {
            return $@"You are a helpful assistant that can use tools to complete tasks.

                    AVAILABLE TOOLS:
                    <tools>
                    {_toolRegistry}
                    </tools>

                    FORMAT YOUR RESPONSES USING THESE TAGS:
                    <thought>Your reasoning about what to do next</thought>
                    <tool_call>{{""name"": ""ToolName"", ""arguments"": {{""param"": ""value""}}, ""id"": ""1""}}</tool_call>
                    <response>Your final answer to the user</response>

                    CRITICAL RULES:
                    1. If a tool call fails, say ""I couldn't get [specific data] because the tool failed""
                    2. Never make up or guess information - only use actual tool results
                    3. Use simple, clear tool calls - double-check your JSON format
                    4. If you can't complete part of a task, explain what you could and couldn't do

                    EXAMPLE:
                    User: Get weather for Paris and a random number 1-10

                    <thought>I need weather for Paris and a random number. Let me call both tools.</thought>
                    <tool_call>{{""name"": ""GetWeather"", ""arguments"": {{""location"": ""Paris""}}, ""id"": ""1""}}</tool_call>
                    <tool_call>{{""name"": ""GenerateRandom"", ""arguments"": {{""min"": 1, ""max"": 10}}, ""id"": ""2""}}</tool_call>

                    (After getting results...)
                    <response>The weather in Paris is 22°C and sunny. Your random number is: 7</response>

                    If a tool fails:
                    <response>I got the weather (22°C, sunny) but couldn't generate the random number because the tool failed.</response>";
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
            //if (text.Contains("<tool_call>") && !text.Contains("</tool_call>")) text += "</tool_call>";

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