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
    public enum ToolCallMode
    {
        None,     // expose tools but forbid calls (Thought stage)
        Auto,     // allow text or tool calls (Action stage)
        Required  // force tool call (rare, like guardrails)
    }
    public interface IToolCoordinator
    {
        Task<ToolCall> GetToolCall(Conversation prompt, ToolCallMode toolCallMode = ToolCallMode.Auto, int maxRetries = 0, params Tool[] tools);
        Task<ToolCall> GetToolCall<TTuple>(Conversation prompt, ToolCallMode toolCallMode = ToolCallMode.Auto, int maxRetries = 0);
        ToolCall? TryExtractInlineToolCall(string content, bool strict = false);
        Task<T?> GetStructuredResponse<T>(Conversation prompt, int maxRetries = 3);
        Task<string?> ExecuteToolCall(Conversation chat, params Tool[] tools);
        Task<T?> ExecuteToolCall<T>(Conversation chat, params Tool[] tools);
        Task<string> HandleToolCall(ToolCall toolCall);
        Task<dynamic> Invoke(ToolCall tool);
        Task<T?> Invoke<T>(ToolCall tool);
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

        public async Task<ToolCall> GetToolCall(
           Conversation prompt,
           ToolCallMode toolCallMode = ToolCallMode.Auto,
           int maxRetries = 0) => await GetToolCall(
            prompt,
            toolCallMode,
            maxRetries,
            toolRegistry.RegisteredTools.ToArray());
        public async Task<ToolCall> GetToolCall<TTuple>(
            Conversation prompt,
            ToolCallMode toolCallMode = ToolCallMode.Auto,
            int maxRetries = 0)
        {
            var tupleType = typeof(TTuple);

            // Single type (non-tuple)
            if (!tupleType.IsGenericType ||
                tupleType.GetGenericTypeDefinition() != typeof(ValueTuple<>) &&
                 !tupleType.FullName!.StartsWith("System.ValueTuple"))
            {
                var single = toolRegistry.GetTools(tupleType).ToArray();
                return await GetToolCall(prompt, toolCallMode, maxRetries, single);
            }

            // Multiple types inside tuple
            var types = tupleType.GetGenericArguments();
            var toolSubSet = toolRegistry.GetTools(types).ToArray();

            return await GetToolCall(prompt, toolCallMode, maxRetries, toolSubSet);
        }

        public async Task<ToolCall> GetToolCall(
    Conversation prompt,
    ToolCallMode toolCallMode = ToolCallMode.Auto,
    int maxRetries = 0,
    params Tool[] tools)
        {
            if (tools == null || !tools.Any())
                throw new ArgumentException("No tools available.", nameof(tools));

            var intPrompt = Conversation.Clone(prompt);

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                var response = await llm.GetToolCallResponse(intPrompt, tools, toolCallMode);

                try
                {
                    if (response != null)
                    {
                        if (tools.Any(t => t.Name == response.Name))
                        {
                            return new ToolCall(
                                response.Id ?? Guid.NewGuid().ToString(),
                                response.Name,
                                response.Arguments,
                                ParseToolParams(response.Name, response.Arguments),
                                response.AssistantMessage
                            );
                        }

                        if (!string.IsNullOrWhiteSpace(response.AssistantMessage))
                        {
                            var toolCall = TryExtractInlineToolCall(response.AssistantMessage);
                            if (toolCall != null) return toolCall;

                            intPrompt.Add(Role.System,
                                $"Invalid format. Return one tool call as JSON: {{id, name, arguments}}. " +
                                $"Valid tools: {string.Join(", ", tools.Select(t => t.Name))}.");
                            continue;
                        }
                    }
                }
                catch
                {
                    intPrompt.Add(Role.System,
                        $"Arguments error. Return valid JSON tool call with fields: id, name, arguments. " +
                        $"Tools: {string.Join(", ", tools.Select(t => t.Name))}.");
                    continue;
                }

                intPrompt.Add(Role.System,
                    $"Empty or invalid. Return exactly one JSON tool call. " +
                    $"Tools: {string.Join(", ", tools.Select(t => t.Name))}.");
            }

            return new("No valid tool call produced. Please refine your request.");
        }

        public async Task<T> GetStructuredResponse<T>(string systemPrompt, string userContent = "", ILogger? logger = null)
        {
            var gateChat = new Conversation()
                .Add(Role.System, systemPrompt)
                .Add(Role.User, userContent);

            logger?.AttachTo(gateChat, $"Gate:{typeof(T).Name}");

            return await GetStructuredResponse<T>(gateChat);
        }
        public async Task<T?> GetStructuredResponse<T>(Conversation prompt, int maxRetries = 3)
        {
            var intPrompt = Conversation.Clone(prompt);

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                try
                {
                    var jsonResponse = await llm.GetStructuredResponse(intPrompt, JsonSchemaExtensions.GetSchemaFor<T>());
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
        public async Task<string> GetTextResponse(
    Conversation prompt,
    int maxRetries = 3,
    params Tool[]? tools)
        {
            var intPrompt = Conversation.Clone(prompt);

            // if caller gave no explicit tools → use all registered
            if (tools == null || tools.Length == 0)
                tools = toolRegistry.RegisteredTools.ToArray();

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                try
                {
                    // this always forces ToolCallMode.None → text only
                    var toolCall = await llm.GetToolCallResponse(intPrompt, tools, ToolCallMode.None);

                    if (!string.IsNullOrWhiteSpace(toolCall.AssistantMessage))
                        return toolCall.AssistantMessage;

                    // if LLM somehow tried to sneak a tool call, ignore it and retry
                    intPrompt.Add(Role.System,
                        "Do not call a tool in this turn. Provide only text reasoning/answer.");
                }
                catch (Exception ex)
                {
                    if (attempt == maxRetries) throw;

                    intPrompt.Add(Role.System,
                        $"Error: {ex.Message}. Provide only plain text this turn.");
                }
            }

            return string.Empty;
        }

        public ToolCall? TryExtractInlineToolCall(string content, bool strict = false)
        {
            var matches = ToolTagPattern.Matches(content).Cast<Match>()
                .Concat(LooseToolJsonPattern.Matches(content).Cast<Match>())
                .Concat(MessageOnlyPattern.Matches(content).Cast<Match>())
                .OrderBy(m => m.Index)
                .ToList();

            string fallback = matches.Any() ? content.Substring(0, matches[0].Index).Trim() : content.Trim();

            foreach (var match in matches)
            {
                string jsonStr = match.Groups["json"].Value.Trim();
                JsonObject? node = null;
                try { node = JsonNode.Parse(jsonStr)?.AsObject(); }
                catch
                {
                    if (strict) return new ToolCall($"Invalid JSON format in tool call: `{jsonStr}`");
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
                        return new ToolCall("Tool call missing required 'name' field.");
                    }
                    if (!toolRegistry.Contains(name))
                    {
                        return new ToolCall($"Tool `{name}` is not registered. Available tools: {string.Join(", ", toolRegistry.RegisteredTools.Select(t => t.Name))}");
                    }

                    var id = node.ContainsKey("id") && node["id"] != null
                                    ? node["id"]!.ToString()
                                    : Guid.NewGuid().ToString();

                    return new ToolCall(
                            id,
                            name,
                            args,
                            ParseToolParams(name, args),
                            message ?? fallback
                        );
                }

                // Message-only pattern
                if (!hasName && !hasArgs && hasMessage)
                {
                    return new ToolCall(node[ToolJsonAssistantMessageTag]?.ToString() ?? fallback);
                }
                // Detailed error feedback
                if (strict)
                {
                    if (!hasName && hasArgs)
                        return new ToolCall("Tool call has arguments but no 'name' field.");
                    if (hasName && !hasArgs)
                        return new ToolCall($"Tool call for `{node[ToolJsonNameTag]}` is missing 'arguments'.");
                }
            }
            if (strict) return new ToolCall("No valid tool call structure found. Expected { \"name\": ..., \"arguments\": {...} }");

            return string.IsNullOrEmpty(fallback) ? null : new ToolCall(fallback);
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


        // Non-generic overload: defaults to string
        public async Task<string?> ExecuteToolCall(
            Conversation chat,
            params Tool[] tools)
        {
            return await ExecuteToolCall<string>(chat, tools: tools) ?? "";
        }
        // Generic version
        public async Task<T?> ExecuteToolCall<T>(
            Conversation chat,
            params Tool[] tools)
        {
            var toolCall = await GetToolCall(chat, tools: tools);

            // Log assistant message
            if (!string.IsNullOrWhiteSpace(toolCall.AssistantMessage))
            {
                Console.WriteLine($"[TOOLCALL] Assistant message: {toolCall.AssistantMessage}");
                chat.Add(Role.Assistant, toolCall.AssistantMessage);
            }

            T? result = default;

            // Invoke tool if a tool name exists
            if (!string.IsNullOrWhiteSpace(toolCall.Name))
            {
                Console.WriteLine($"[TOOLCALL] Invoking tool: {toolCall.Name} ========================>");
                chat.Add(Role.Assistant, tool: toolCall);

                try
                {
                    // Use T here instead of object
                    result = await Invoke<T>(toolCall);
                    Console.WriteLine($"[TOOL RESULT] {result}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Tool invocation failed: {ex}");
                    chat.Add(Role.Assistant, $"Tool invocation failed - {ex}");
                }

                chat.Add(Role.Tool, result?.ToString(), toolCall);
            }

            return result;
        }

        public async Task<string> HandleToolCall(ToolCall toolCall)
        {
            if (string.IsNullOrEmpty(toolCall.Name) && string.IsNullOrEmpty(toolCall.AssistantMessage))
            {
                return "No valid tool call provided";
            }
            else if (!string.IsNullOrWhiteSpace(toolCall.AssistantMessage))
            {
                return toolCall.AssistantMessage;
            }

            try
            {
                var result = await Invoke(toolCall);
                return result.AsString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Tool invocation failed: {ex}");
                return $"Tool execution error: {ex.Message}";
            }
        }


        // Non-generic: automatically returns string for complex objects
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

            return result.ToString();
        }

        public async Task<T?> Invoke<T>(ToolCall tool)
        {
            var result = await Invoke(tool); // call non-generic version
            if (result == null) return default;

            if (result is T typedResult) return typedResult;

            if (typeof(T) == typeof(string))
                return (T)(object)result.ToString()!;

            throw new InvalidCastException($"Expected result of type {typeof(T).Name}, got {result.GetType().Name}.");
        }

    }

}
