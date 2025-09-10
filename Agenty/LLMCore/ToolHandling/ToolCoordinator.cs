using Agenty.LLMCore.JsonSchema;
using Agenty.LLMCore.Logging;
using OpenAI.Chat;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Agenty.LLMCore.ToolHandling
{
    public interface IToolCoordinator
    {
        Task<LLMResponse> GetToolCalls(
            Conversation prompt,
            ToolCallMode toolCallMode = ToolCallMode.Auto,
            int maxRetries = 0,
            LLMMode mode = LLMMode.Balanced,  // 🔑 added
            params Tool[] tools);

        LLMResponse TryExtractInlineToolCall(string content, bool strict = false);

        Task<T?> GetStructuredResponse<T>(
            Conversation prompt,
            int maxRetries = 3,
            LLMMode mode = LLMMode.Deterministic); // 🔑 added

        Task HandleToolCall(List<ToolCall> toolCall, Conversation chat);
        Task<dynamic> Invoke(ToolCall tool);
    }
    internal class ToolCoordinator(ILLMClient llm, IToolRegistry toolRegistry) : IToolCoordinator
    {
        private const string ToolJsonNameTag = "name";
        private const string ToolJsonArgumentsTag = "arguments";
        private const string ToolJsonAssistantMessageTag = "message";
        #region regex
        // Match any tag that includes "tool" and contains a well-formed JSON block
        static readonly Regex TagPattern = new Regex(
            @"(?i)                # Ignore case
            [\[\{\(<]             # opening bracket of any type
            [^\]\}\)>]*?          # non-greedy anything except closing brackets
            \b\w*tool\w*\b        # word 'tool' inside (word boundary optional)
            [^\]\}\)>]*?          # again anything before closing
            [\]\}\)>]             # closing bracket
            ",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace
        );

        static readonly Regex ToolTagPattern = new Regex(
            $@"(?ix)                  # Ignore case + Ignore whitespace
            (?<open>{TagPattern})     # opening tag like [TOOL_REQUEST]
            \s*                       # optional whitespace/newlines
            (?<json>\{{[\s\S]*?\}})   # JSON object
            \s*                       # optional whitespace/newlines
            (?<close>{TagPattern})    # closing tag like [END_TOOL_REQUEST]
            ",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace
        );

        static readonly Regex LooseToolJsonPattern = new Regex(
            $@"
            (?<json>
                \{{
                  \s*""{ToolJsonNameTag}""\s*:\s*""[^""]+""
                  \s*,\s*
                  ""{ToolJsonArgumentsTag}""\s*:\s*\{{[\s\S]*\}}
                  (?:\s*,\s*""[^""]+""\s*:\s*[^}}]+)*   # allow extra props like {ToolJsonAssistantMessageTag}
                  \s*
                \}}
            )
            ",
            RegexOptions.Compiled | RegexOptions.IgnorePatternWhitespace | RegexOptions.IgnoreCase
        );

        static readonly Regex MessageOnlyPattern = new Regex(
            $@"
            (?<json>
                \{{
                  \s*""{ToolJsonAssistantMessageTag}""\s*:\s*""[^""]+""
                  \s*
                \}}
            )
            ",
            RegexOptions.Compiled | RegexOptions.IgnorePatternWhitespace | RegexOptions.IgnoreCase
        );
        #endregion

        public async Task<LLMResponse> GetToolCalls(
    Conversation prompt,
    ToolCallMode toolCallMode = ToolCallMode.Auto,
    int maxRetries = 3,
    LLMMode mode = LLMMode.Balanced,
    params Tool[] tools)
        {
            tools = tools?.Any() == true ? tools : toolRegistry.RegisteredTools.ToArray();
            if (tools.Length == 0) throw new ArgumentException("No tools available.", nameof(tools));

            var intPrompt = Conversation.Clone(prompt);

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                var response = await llm.GetToolCallResponse(intPrompt, tools, toolCallMode, mode);

                try
                {
                    var valid = new List<ToolCall>();
                    if (response.ToolCalls.Any())
                    {
                        foreach (var toolCall in response.ToolCalls)
                        {
                            if (toolRegistry.Contains(toolCall.Name))
                            {
                                if (prompt.IsToolAlreadyCalled(toolCall))
                                {
                                    string lastResult = prompt.GetLastToolCallResult(toolCall);

                                    intPrompt.Add(Role.User,
                                        $"Tool `{toolCall.Name}` was already called with the same arguments. " +
                                        $"The result was: {lastResult}. ");
                                    continue;
                                }

                                valid.Add(new ToolCall(
                                    toolCall.Id ?? Guid.NewGuid().ToString(),
                                    toolCall.Name,
                                    toolCall.Arguments,
                                    ParseToolParams(toolCall.Name, toolCall.Arguments)
                                ));
                            }
                            else if (!string.IsNullOrWhiteSpace(response.AssistantMessage))
                            {
                                if (prompt.IsLastAssistantMessageSame(response.AssistantMessage))
                                {
                                    intPrompt.Add(Role.User,
                                        "You just gave the exact same assistant message. Don't repeat yourself — build on the previous response instead.");
                                    continue;
                                }

                                var LLMResponse = TryExtractInlineToolCall(response.AssistantMessage);
                                if (LLMResponse != null) return LLMResponse;
                            }
                        }
                    }

                    if (valid.Count > 0 || !string.IsNullOrWhiteSpace(response.AssistantMessage))
                    {
                        return new LLMResponse
                        {
                            AssistantMessage = response.AssistantMessage,
                            ToolCalls = valid,
                            FinishReason = response.FinishReason
                        };
                    }
                }
                catch
                {
                    intPrompt.Add(Role.User,
                        $"Arguments error. Return valid JSON tool call with fields: id, name, arguments. " +
                        $"Tools: {string.Join(", ", tools.Select(t => t.Name))}.");
                    continue;
                }

                intPrompt.Add(Role.User,
                    $"Empty or invalid tool call. " +
                    $"Tools: {string.Join(", ", tools.Select(t => t.Name))}.");
            }

            return new LLMResponse("no tool call produced");
        }

        public async Task<T?> GetStructuredResponse<T>(
            Conversation prompt,
            int maxRetries = 3,
            LLMMode mode = LLMMode.Deterministic) // 🔑 Deterministic by default
        {
            var intPrompt = Conversation.Clone(prompt);

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                try
                {
                    // 🔑 Pass AgentMode down
                    var jsonResponse = await llm.GetStructuredResponse(
                        intPrompt,
                        JsonSchemaExtensions.GetSchemaFor<T>(),
                        mode);

                    if (jsonResponse != null)
                    {
                        var jsonString = jsonResponse.ToJsonString();
                        var result = JsonHelper.DeserializeJson<T>(jsonString);
                        if (result != null) return result;
                    }
                }
                catch (Exception ex)
                {
                    if (attempt == maxRetries) throw;

                    intPrompt.Add(Role.Assistant,
                        $"The last response failed with [{ex.Message}]. Please provide a valid JSON response matching the schema.");
                    continue;
                }

                intPrompt.Add(Role.Assistant,
                    $"The last response was empty or invalid. Please return a valid JSON response for type {typeof(T).Name}.");
            }

            return default;
        }

        public LLMResponse TryExtractInlineToolCall(string content, bool strict = false)
        {
            var matches = ToolTagPattern.Matches(content).Cast<Match>()
                .Concat(LooseToolJsonPattern.Matches(content).Cast<Match>())
                .Concat(MessageOnlyPattern.Matches(content).Cast<Match>())
                .OrderBy(m => m.Index)
                .ToList();

            string fallback = matches.Any()
                ? content.Substring(0, matches[0].Index).Trim()
                : content.Trim();

            var response = new LLMResponse();

            foreach (var match in matches)
            {
                string jsonStr = match.Groups["json"].Value.Trim();
                JsonObject? node = null;
                try { node = JsonNode.Parse(jsonStr)?.AsObject(); }
                catch
                {
                    if (strict)
                    {
                        response.AssistantMessage = $"Invalid JSON format in tool call: `{jsonStr}`";
                        return response;
                    }
                    continue;
                }

                if (node == null) continue;

                var hasName = node.ContainsKey(ToolJsonNameTag);
                var hasArgs = node.ContainsKey(ToolJsonArgumentsTag);
                var hasMessage = node.ContainsKey(ToolJsonAssistantMessageTag);

                // Tool call with name & args
                if (hasName && hasArgs)
                {
                    var name = node[ToolJsonNameTag]!.ToString();
                    var args = node[ToolJsonArgumentsTag] is JsonObject argObj
                                ? JsonNode.Parse(argObj.ToJsonString())!.AsObject()
                                : new JsonObject();
                    var message = node[ToolJsonAssistantMessageTag]?.ToString();

                    if (string.IsNullOrEmpty(name))
                    {
                        response.AssistantMessage = "Tool call missing required 'name' field.";
                        return response;
                    }
                    if (!toolRegistry.Contains(name))
                    {
                        response.AssistantMessage =
                            $"Tool `{name}` is not registered. Available tools: {string.Join(", ", toolRegistry.RegisteredTools.Select(t => t.Name))}";
                        return response;
                    }

                    var id = node.ContainsKey("id") && node["id"] != null
                        ? node["id"]!.ToString()
                        : Guid.NewGuid().ToString();

                    response.ToolCalls.Add(new ToolCall(
                        id,
                        name,
                        args,
                        ParseToolParams(name, args),
                        message
                    ));
                    continue;
                }

                // Message-only pattern
                if (!hasName && !hasArgs && hasMessage)
                {
                    response.ToolCalls.Add(new(node[ToolJsonAssistantMessageTag]?.ToString() ?? fallback));
                    return response;
                }

                // Strict error feedback
                if (strict)
                {
                    if (!hasName && hasArgs)
                    {
                        response.ToolCalls.Add(new("Tool call has arguments but no 'name' field."));
                        return response;
                    }
                    if (hasName && !hasArgs)
                    {
                        response.ToolCalls.Add(new($"Tool call for `{node[ToolJsonNameTag]}` is missing 'arguments'."));
                        return response;
                    }
                }
            }

            if (response.ToolCalls.Count == 0 && strict)
                response.ToolCalls.Add(new("No valid tool call structure found. Expected { \"name\": ..., \"arguments\": {...} }"));


            if (!string.IsNullOrWhiteSpace(fallback)) response.AssistantMessage = fallback;

            return response;

        }

        private object?[] ParseToolParams(string toolName, JsonObject arguments)
        {
            var tool = toolRegistry.Get(toolName);
            if (tool?.Function == null)
                throw new InvalidOperationException($"Tool '{toolName}' not registered or has no function.");

            var func = tool.Function!;
            var method = func.Method;
            var methodParams = method.GetParameters();
            var argsObj = arguments ?? throw new ArgumentException("ToolCallInfo.Parameters is null");

            // Handle case where parameters are passed as a single wrapped object
            if (methodParams.Length == 1 && !methodParams[0].ParameterType.IsSimpleType() &&
                !argsObj.ContainsKey(methodParams[0].Name!))
            {
                argsObj = new JsonObject { [methodParams[0].Name!] = argsObj };
            }

            var paramValues = new object?[methodParams.Length];
            for (int i = 0; i < methodParams.Length; i++)
            {
                var p = methodParams[i];
                if (!argsObj.TryGetPropertyValue(p.Name!, out var node) || node == null)
                {
                    if (p.HasDefaultValue) paramValues[i] = p.DefaultValue;
                    else if (!p.ParameterType.IsValueType || Nullable.GetUnderlyingType(p.ParameterType) != null) paramValues[i] = null;
                    else throw new ArgumentException($"Missing required parameter '{p.Name}' with no default value.");
                }
                else
                {
                    try
                    {
                        paramValues[i] = JsonHelper.DeserializeJson(node.ToJsonString(), p.ParameterType);
                    }
                    catch
                    {
                        var value = $"'{node.ToString()}'" ?? "";
                        var parmDesc = p?.GetCustomAttribute<DescriptionAttribute>()?.Description is string d && !string.IsNullOrWhiteSpace(d) ? $"({d})" : "";
                        var paramTypeDesc = p?.ParameterType.GetCustomAttribute<DescriptionAttribute>()?.Description is string d2 && !string.IsNullOrWhiteSpace(d2) ? $"({d2})" : "";

                        throw new ArgumentException($"Value {value} for '{p?.Name}' {parmDesc} may be invalid for {p?.ParameterType.Name} {paramTypeDesc}");
                    }

                }
            }

            return paramValues;
        }


        public async Task HandleToolCall(List<ToolCall> toolCalls, Conversation chat)
        {
            if (toolCalls == null || toolCalls.Count == 0)
                return;

            // Special case: if this was really just a "message" with no name (assistant text instead of a tool call)
            if (toolCalls.Count == 1 &&
                string.IsNullOrWhiteSpace(toolCalls[0].Name) &&
                !string.IsNullOrWhiteSpace(toolCalls[0].Message))
            {
                chat.Add(Role.Assistant, toolCalls[0].Message);
                return;
            }

            // Add one assistant message containing ALL tool calls
            chat.Add(Role.Assistant, toolCalls: toolCalls);

            // Execute tools in parallel
            var tasks = toolCalls.Select(async call =>
            {
                try
                {
                    var result = await Invoke(call);
                    chat.Add(Role.Tool, ((object?)result).AsJSONString(), new List<ToolCall> { call });
                }
                catch (Exception ex)
                {
                    chat.Add(Role.Tool, $"Tool execution error: {ex.Message}", new List<ToolCall> { call });
                }
            });

            await Task.WhenAll(tasks);
        }

        public async Task HandleToolCallSequential(List<ToolCall> toolCalls, Conversation chat)
        {
            if (toolCalls == null || toolCalls.Count == 0)
                return;

            foreach (var call in toolCalls)
            {
                // Case: plain assistant text instead of tool call
                if (string.IsNullOrWhiteSpace(call.Name) && !string.IsNullOrWhiteSpace(call.Message))
                {
                    chat.Add(Role.Assistant, call.Message);
                    continue;
                }

                // Add assistant message with *this one tool call*
                chat.Add(Role.Assistant, null, toolCall: call);

                // Execute synchronously and add tool result
                try
                {
                    var result = await Invoke(call);
                    chat.Add(Role.Tool, ((object?)result).AsJSONString(), toolCall: call);
                }
                catch (Exception ex)
                {
                    chat.Add(Role.Tool, $"Tool execution error: {ex.Message}", toolCall: call);
                }
            }
        }


        public async Task<dynamic> Invoke(ToolCall tool)
        {
            if (tool == null) throw new ArgumentNullException(nameof(tool));
            var paramValues = tool.Parameters;

            var func = toolRegistry.Get(tool.Name)?.Function!;
            var method = func.Method;
            var returnType = method.ReturnType;

            object? result = null;

            // Handle async methods
            if (typeof(Task).IsAssignableFrom(returnType))
            {
                var task = (Task)func.DynamicInvoke(paramValues)!;
                await task.ConfigureAwait(false);

                if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
                {
                    var resultProperty = returnType.GetProperty("Result")!;
                    result = resultProperty.GetValue(task);
                }
            }
            else
            {
                // Sync return
                result = func.DynamicInvoke(paramValues);
            }

            if (result == null) return null;

            // Return primitives as-is, strings as-is, else ToString() for complex objects
            if (result is string || result.GetType().IsPrimitive)
                return result;

            return result;
        }

    }

}
