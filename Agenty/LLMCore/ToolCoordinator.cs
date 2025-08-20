using Agenty.LLMCore.JsonSchema;
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

namespace Agenty.LLMCore
{
    internal class ToolCoordinator(ILLMClient llm, IToolRegistry toolRegistry)
    {
        private const string JsonName = "name";
        private const string JsonArguments = "arguments";
        private const string JsonMessage = "message";
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
                  \s*""{JsonName}""\s*:\s*""[^""]+""
                  \s*,\s*
                  ""{JsonArguments}""\s*:\s*\{{[\s\S]*\}}
                  (?:\s*,\s*""[^""]+""\s*:\s*[^}}]+)*   # allow extra props like {JsonMessage}
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
                  \s*""{JsonMessage}""\s*:\s*""[^""]+""
                  \s*
                \}}
            )
            ",
            RegexOptions.Compiled | RegexOptions.IgnorePatternWhitespace | RegexOptions.IgnoreCase
        );
        #endregion


        public async Task<ToolCall> GetToolCall(
           Conversation prompt,
           bool forceToolCall = false,
           int maxRetries = 0) => await GetToolCall(
            prompt,
            forceToolCall,
            maxRetries,
            toolRegistry.RegisteredTools.ToArray());
        public async Task<ToolCall> GetToolCall<TTuple>(
            Conversation prompt,
            bool forceToolCall = false,
            int maxRetries = 0)
        {
            var tupleType = typeof(TTuple);

            // Single type (non-tuple)
            if (!tupleType.IsGenericType ||
                (tupleType.GetGenericTypeDefinition() != typeof(ValueTuple<>) &&
                 !tupleType.FullName!.StartsWith("System.ValueTuple")))
            {
                var single = toolRegistry.GetTools(tupleType).ToArray();
                return await GetToolCall(prompt, forceToolCall, maxRetries, single);
            }

            // Multiple types inside tuple
            var types = tupleType.GetGenericArguments();
            var toolSubSet = toolRegistry.GetTools(types).ToArray();

            return await GetToolCall(prompt, forceToolCall, maxRetries, toolSubSet);
        }

        public async Task<ToolCall> GetToolCall(
            Conversation prompt,
            bool forceToolCall = false,
            int maxRetries = 0,
            params Tool[] tools)
        {
            if (tools == null || !tools.Any())
                throw new ArgumentNullException(nameof(tools), "No tools provided for function call response.");

            var intPrompt = Conversation.Clone(prompt);

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                var response = await llm.GetToolCallResponse(intPrompt, tools, forceToolCall);

                try
                {
                    if (response != null)
                    {
                        if (tools.Any(t => t.Name == response.Name))
                        {
                            var name = response.Name;
                            var args = response.Arguments;

                            Console.WriteLine("=========================================");
                            Console.WriteLine(name);
                            Console.WriteLine(args);
                            Console.WriteLine("=========================================");

                            return new ToolCall(
                                response.Id ?? Guid.NewGuid().ToString(),
                                name,
                                args,
                                ParseToolParams(name, args),
                                response.AssistantMessage
                            );
                        }

                        string? content = response.AssistantMessage;
                        if (!string.IsNullOrWhiteSpace(content))
                        {
                            var toolCall = TryExtractInlineToolCall(content);
                            if (toolCall != null) return toolCall;

                            return new ToolCall(content);
                        }
                    }
                }
                catch (Exception ex)
                {
                    intPrompt.Add(Role.Assistant, $"The last response failed with [{ex.Message}].");
                    continue;
                }

                intPrompt.Add(
                    Role.Assistant,
                    $"The last response was empty or invalid. " +
                    $"Please return a valid tool call using one of: {string.Join(", ", tools.Select(t => t.Name))}."
                );
            }

            return new ToolCall("Couldn't generate a valid tool call/response.");
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


        private ToolCall? TryExtractInlineToolCall(string content)
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
                catch { /* invalid JSON, skip */ }

                if (node == null) continue;

                var hasName = node.ContainsKey(JsonName);
                var hasArgs = node.ContainsKey(JsonArguments);
                var hasMessage = node.ContainsKey(JsonMessage);

                // Tool call with name & args
                if (hasName && hasArgs)
                {
                    var name = node[JsonName]!.ToString();
                    var args = node[JsonArguments] as JsonObject ?? new JsonObject();
                    var message = node[JsonMessage]?.ToString();

                    if (!string.IsNullOrEmpty(name) && toolRegistry.Contains(name))
                    {
                        // First valid tool found -> return immediately
                        return new ToolCall(
                            Guid.NewGuid().ToString(),
                            name,
                            args,
                            ParseToolParams(name, args),
                            message ?? fallback
                        );
                    }
                }

                // Message-only pattern
                if (!hasName && !hasArgs && hasMessage)
                {
                    return new ToolCall(node[JsonMessage]?.ToString() ?? fallback);
                }
            }

            // No valid tool call found -> fallback to text before first match
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
                        var parmDesc = (p?.GetCustomAttribute<DescriptionAttribute>()?.Description is string d && !string.IsNullOrWhiteSpace(d)) ? $"({d})" : "";
                        var paramTypeDesc = (p?.ParameterType.GetCustomAttribute<DescriptionAttribute>()?.Description is string d2 && !string.IsNullOrWhiteSpace(d2)) ? $"({d2})" : "";

                        throw new ArgumentException($"Value {value} for '{p?.Name}' {parmDesc} may be invalid for {p?.ParameterType.Name} {paramTypeDesc}");
                    }

                }
            }

            return paramValues;
        }

        public async Task<T?> Invoke<T>(ToolCall tool)
        {
            if (tool == null) throw new ArgumentNullException(nameof(tool));
            var paramValues = tool.Parameters;

            var func = toolRegistry.Get(tool.Name)?.Function!;
            var method = func.Method;
            var returnType = method.ReturnType;

            // Handle async methods (Task or Task<T>)
            if (typeof(Task).IsAssignableFrom(returnType))
            {
                var task = (Task)func.DynamicInvoke(paramValues)!;
                await task.ConfigureAwait(false);

                if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
                {
                    var resultProperty = returnType.GetProperty("Result");
                    var taskResult = resultProperty!.GetValue(task);
                    return (T?)taskResult;
                }
                else
                {
                    return default; // For Task (non-generic)
                }
            }
            else
            {
                // Sync return
                var result = func.DynamicInvoke(paramValues);
                if (result == null) return default;
                if (result is T typedResult) return typedResult;
                throw new InvalidCastException($"Expected result of type {typeof(T).Name}, got {result.GetType().Name}.");
            }
        }
    }

}
