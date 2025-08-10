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
    internal class ToolCoordinator(ILLMClient llm)
    {
        public Task<ToolCall> GetDefaultToolCall(ChatHistory prompt, params Tool[] tools)
            => GetDefaultToolCall(prompt, new ToolManager(tools));
        public async Task<ToolCall> GetDefaultToolCall(ChatHistory prompt, ITools tools, bool forceToolCall = false, int maxRetries = 3)
        {
            if (tools == null || !tools.RegisteredTools.Any()) throw new ArgumentNullException(nameof(tools), "No tools provided for function call response.");

            var intPrompt = ChatHistory.Clone(prompt);
            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                var response = await llm.GetToolCallResponse(intPrompt, tools.RegisteredTools, forceToolCall);

                try
                {
                    if (response != null)
                    {
                        if (tools.Contains(response.Name))
                        {
                            var name = response.Name;
                            var args = response.Arguments;
                            return new
                            (
                                response.Id ?? Guid.NewGuid().ToString(),
                                name,
                                args,
                                ParseToolParams(name, tools, args),
                                response.AssistantMessage
                            );
                        }

                        string? content = response.AssistantMessage;
                        if (!string.IsNullOrWhiteSpace(content))
                        {
                            var toolCall = TryExtractInlineToolCall(content, tools);
                            if (toolCall != null) return toolCall;
                            return new(content);
                        }
                    }
                }
                catch (Exception ex)
                {
                    intPrompt.Add(Role.Assistant, $"The last response failed with [{ex.Message}].");
                    continue;
                }

                intPrompt.Add(Role.Assistant, $"The last response was empty or invalid. Please return a valid tool call using one of: {string.Join(", ", tools.RegisteredTools.Select(t => t.Name))}.");
            }

            return new("Couldn't generate a valid tool call/response.");
        }

        public async Task<ToolCall> GetStructuredToolCall(ChatHistory prompt, ITools tools, int maxRetries = 3)
        {
            if (tools == null || !tools.RegisteredTools.Any()) throw new ArgumentNullException(nameof(tools), "No tools provided for function call response.");

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                var intPrompt = new ChatHistory();

                var systemPrompt = BuildSystemPrompt(tools, attempt > 0);
                intPrompt.Add(Role.System, systemPrompt);
                intPrompt.AddRange(prompt.Where(m => m.Role != Role.System));

                try
                {
                    var response = await llm.GetStructuredResponse(intPrompt, GetToolCallSchema(tools));
                    if (response != null)
                    {
                        var content = response.ToJsonString();
                        var toolCall = TryExtractInlineToolCall(content, tools);
                        if (toolCall != null) return toolCall;
                    }
                }
                catch (Exception ex)
                {
                    intPrompt.Add(Role.Assistant, $"The last response failed with [{ex.Message}].");
                    continue;
                }

                intPrompt.Add(Role.Assistant, $"The last response was empty or invalid. Please return a valid tool call using one of: {string.Join(", ", tools.RegisteredTools.Select(t => t.Name))}.");
            }

            return new("Couldn't generate a valid tool call/response.");
        }

        private string BuildSystemPrompt(ITools tools, bool isRetry)
        {
            var toolsList = string.Join(", ", tools.RegisteredTools.Select(t => t.Name));
            //Available tools: {toolsList}
            var prompt = $@"

            When the user's question requires using a tool, respond with a JSON object to call that tool exactly once.
            After the tool call, do not call any other tools or repeat calls.

            JSON formats:

            - To call a tool, respond with:
              {{
                ""name"": ""tool_name"",
                ""arguments"": {{ ... }},
                ""message"": """"
              }}

            - To respond directly without calling any tool, respond with:
              {{
                ""message"": ""your answer here""
              }}

            Only call a tool if necessary to answer the question. Otherwise, reply directly with the message.

            Do not repeat tool calls or combine multiple tools in one response.";

            if (isRetry) prompt += "\nRetry: respond only with valid JSON in the formats described above.";

            return prompt;
        }

        #region regex
        // Match any tag that includes "tool" and contains a well-formed JSON block
        static readonly string tagPattern = @"(?i)
            [\[\{\(<]         # opening bracket of any type
            [^\]\}\)>]*?      # non-greedy anything except closing brackets
            \b\w*tool\w*\b    # word “tool” inside (word boundary optional if you want partials)
            [^\]\}\)>]*?      # again anything before closing
            [\]\}\)>]         # closing bracket
        ";

        static readonly string toolTagPattern = @$"(?ix)
            (?<open>{tagPattern})         # opening tag like [TOOL_REQUEST]
            \s*                           # optional whitespace/newlines
            (?<json>\{{[\s\S]*?\}})         # JSON object
            \s*                           # optional whitespace/newlines
            (?<close>{tagPattern})        # closing tag like [END_TOOL_REQUEST]
        ";

        static readonly string looseToolJsonPattern = @"
            (?<json>
                \{
                  \s*""name""\s*:\s*""[^""]+""
                  \s*,\s*
                  ""arguments""\s*:\s*\{[\s\S]*\}
                  (?:\s*,\s*""[^""]+""\s*:\s*[^}]+)*   # allow extra props like message
                  \s*
                \}
            )
        ";
        static readonly string messageOnlyPattern = @"
            (?<json>
                \{
                  \s*""message""\s*:\s*""[^""]+""
                  \s*
                \}
            )
        ";
        #endregion
        private ToolCall? TryExtractInlineToolCall(string content, ITools tools)
        {
            var opts = RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace;

            var match = Regex.Matches(content, toolTagPattern, opts)
                 .Cast<Match>()
                 .Concat(Regex.Matches(content, looseToolJsonPattern, opts).Cast<Match>())
                 .Concat(Regex.Matches(content, messageOnlyPattern, opts).Cast<Match>())
                 .OrderBy(m => m.Index)
                 .FirstOrDefault();

            string jsonStr;
            string cleaned = "";

            if (match != null)
            {
                // Found regex pattern match
                cleaned = content.Substring(0, match.Index).Trim();
                jsonStr = match.Groups["json"].Value.Trim();
            }
            else
            {
                // No regex match - try parsing entire content as JSON (for structured responses)
                jsonStr = content.Trim();
                // No cleaned text since we're using the whole content
            }

            Console.WriteLine("=======================");
            Console.WriteLine("RAW extracted jsonStr:");
            Console.WriteLine(jsonStr);
            Console.WriteLine("======================");

            JsonObject? node = null;
            try
            {
                node = JsonNode.Parse(jsonStr)?.AsObject();
            }
            catch { /* invalid JSON */ }

            if (node != null)
            {
                var hasName = node.ContainsKey("name");
                var hasArgs = node.ContainsKey("arguments");
                var hasMessage = node.ContainsKey("message");

                // Case 1: No tool call - just a message (new schema)
                if (!hasName && !hasArgs && hasMessage)
                {
                    return new ToolCall(node["message"]?.ToString() ?? cleaned ?? "No message provided");
                }

                // Case 2: Tool call with name and arguments
                if (hasName && hasArgs)
                {
                    var name = node["name"]!.ToString();
                    var args = node["arguments"] as JsonObject ?? new JsonObject();
                    var message = node["message"]?.ToString();

                    if (string.IsNullOrEmpty(name) || name.Equals("none", StringComparison.OrdinalIgnoreCase))
                    {
                        return new ToolCall(message ?? cleaned ?? "No message provided");
                    }
                    if (tools.Contains(name))
                    {
                        return new(
                            Guid.NewGuid().ToString(),
                            name,
                            args,
                            ParseToolParams(name, tools, args),
                            message ?? cleaned
                        );
                    }
                }
            }
            // Final fallback
            return string.IsNullOrEmpty(cleaned) ? null : new(cleaned);
        }

        private object?[] ParseToolParams(string toolName, ITools tools, JsonObject arguments)
        {
            var tool = tools.Get(toolName);
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
                        paramValues[i] = JsonSerializer.Deserialize(node.ToJsonString(), p.ParameterType, new JsonSerializerOptions
                        {
                            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
                            PropertyNameCaseInsensitive = true
                        });
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
        /// <summary>
        /// The "enum" ensures the name is valid.
        /// The "oneOf" ensures arguments matches exactly one tool schema.
        /// </summary>
        /// <returns>Returns registered tool schema format</returns> 
        private JsonObject GetToolCallSchema(ITools tools)
        {
            var schemaParts = new JsonNode[]
            {
        new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["message"] = new JsonObject { ["type"] = "string" }
            },
            ["required"] = new JsonArray { "message" }
        }
            }
            .Concat(
                tools.RegisteredTools.Select(tool => new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["name"] = new JsonObject { ["const"] = tool.Name },
                        ["arguments"] = JsonNode.Parse(tool.SchemaDefinition?.ToJsonString() ??
                            """{"type": "object", "additionalProperties": false}""")?.AsObject() ??
                            new JsonObject { ["type"] = "object", ["additionalProperties"] = false },
                        ["message"] = new JsonObject { ["type"] = "string" }
                    },
                    ["required"] = new JsonArray { "name", "arguments", "message" }
                })
            );

            return new JsonObject
            {
                ["type"] = "object",
                ["anyOf"] = new JsonArray(schemaParts.ToArray())
            };
        }


        public async Task<T?> Invoke<T>(ToolCall tool, ITools tools)
        {
            if (tool == null) throw new ArgumentNullException(nameof(tool));
            var paramValues = tool.Parameters;

            var func = tools.Get(tool.Name)?.Function!;
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
