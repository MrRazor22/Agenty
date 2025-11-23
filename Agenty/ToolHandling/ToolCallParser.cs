using Agenty.ChatHandling;
using Agenty.JsonSchema;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Agenty.ToolHandling
{
    public sealed class InlineTools
    {
        public List<ToolCall> Calls { get; }
        public string AssistantMessage { get; }

        public InlineTools(List<ToolCall> calls, string assistantMessage)
        {
            Calls = calls;
            AssistantMessage = assistantMessage;
        }
    }

    public interface IToolCallParser
    {
        InlineTools ExtractInlineToolCall(IToolCatalog tools, string content, bool strict = false);
        object[] ParseToolParams(IToolCatalog tools, string toolName, JObject arguments);
        List<ToolValidationError> ValidateAgainstSchema(JToken? node, JObject schema, string path = "");
    }

    public sealed class ToolCallParser : IToolCallParser
    {
        private const string ToolJsonNameTag = "name";
        private const string ToolJsonArgumentsTag = "arguments";
        private const string ToolJsonAssistantMessageTag = "message";

        #region regex
        // Match any tag that includes "tool" and contains a well-formed JSON block
        public static readonly Regex TagPattern = new Regex(
            @"(?i)                # Ignore case
            [\[\{\(<]             # opening bracket of any type
            [^\]\}\)>]*?          # non-greedy anything except closing brackets
            \b\w*tool\w*\b        # word 'tool' inside (word boundary optional)
            [^\]\}\)>]*?          # again anything before closing
            [\]\}\)>]             # closing bracket
            ",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace
        );

        public static readonly Regex ToolTagPattern = new Regex(
            $@"(?ix)                  # Ignore case + Ignore whitespace
            (?<open>{TagPattern})     # opening tag like [TOOL_REQUEST]
            \s*                       # optional whitespace/newlines
            (?<json>\{{[\s\S]*?\}})   # JSON object
            \s*                       # optional whitespace/newlines
            (?<close>{TagPattern})    # closing tag like [END_TOOL_REQUEST]
            ",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace
        );

        public static readonly Regex LooseToolJsonPattern = new Regex(
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

        public static readonly Regex MessageOnlyPattern = new Regex(
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

        public InlineTools ExtractInlineToolCall(IToolCatalog tools, string content, bool strict = false)
        {
            var matches = ToolTagPattern.Matches(content).Cast<Match>()
                .Concat(LooseToolJsonPattern.Matches(content).Cast<Match>())
                .Concat(MessageOnlyPattern.Matches(content).Cast<Match>())
                .OrderBy(m => m.Index)
                .ToList();

            string fallback = matches.Any()
                ? content.Substring(0, matches[0].Index).Trim()
                : content.Trim();

            var toolCalls = new List<ToolCall>();
            string assistantMessage = null;

            foreach (var match in matches)
            {
                string jsonStr = match.Groups["json"].Value.Trim();
                JObject node = null;
                try { node = JObject.Parse(jsonStr); }
                catch
                {
                    if (strict)
                        return new InlineTools(new List<ToolCall>(), $"Invalid JSON: `{jsonStr}`");
                    continue;
                }
                if (node == null) continue;

                var hasName = node.ContainsKey(ToolJsonNameTag);
                var hasArgs = node.ContainsKey(ToolJsonArgumentsTag);
                var hasMessage = node.ContainsKey(ToolJsonAssistantMessageTag);

                if (hasName && hasArgs)
                {
                    var name = node[ToolJsonNameTag]?.ToString();
                    var args = node[ToolJsonArgumentsTag] as JObject ?? new JObject();
                    var message = node[ToolJsonAssistantMessageTag]?.ToString();

                    if (string.IsNullOrEmpty(name))
                        return new InlineTools(new List<ToolCall>(), "Tool call missing 'name'.");

                    if (!tools.Contains(name))
                        return new InlineTools(new List<ToolCall>(),
                            $"Tool `{name}` not registered. Available: {string.Join(", ", tools.RegisteredTools.Select(t => t.Name))}");

                    var id = node.ContainsKey("id")
                        ? node["id"]?.ToString() ?? Guid.NewGuid().ToString()
                        : Guid.NewGuid().ToString();

                    toolCalls.Add(new ToolCall(id, name, args, ParseToolParams(tools, name, args), message));
                    continue;
                }

                if (!hasName && !hasArgs && hasMessage)
                {
                    toolCalls.Add(new ToolCall(node[ToolJsonAssistantMessageTag]?.ToString() ?? fallback));
                    return new InlineTools(toolCalls, assistantMessage);
                }

                if (strict)
                {
                    if (!hasName && hasArgs)
                        toolCalls.Add(new ToolCall("Tool call has arguments but no 'name'."));
                    if (hasName && !hasArgs)
                        toolCalls.Add(new ToolCall($"Tool `{node[ToolJsonNameTag]}` missing 'arguments'."));
                }
            }

            if (toolCalls.Count == 0 && strict)
                toolCalls.Add(new ToolCall("No valid tool call structure found."));

            if (!string.IsNullOrWhiteSpace(fallback))
                assistantMessage = fallback;

            return new InlineTools(toolCalls, assistantMessage);
        }

        public object[] ParseToolParams(IToolCatalog tools, string toolName, JObject arguments)
        {
            var tool = tools.Get(toolName);
            if (tool == null || tool.Function == null)
                throw new InvalidOperationException($"Tool '{toolName}' not registered or has no function.");

            var method = tool.Function.Method;
            var methodParams = method.GetParameters();
            var argsObj = arguments ?? throw new ArgumentException("Arguments null");

            // Wrap single complex param if needed
            if (methodParams.Length == 1 &&
                !methodParams[0].ParameterType.IsSimpleType() &&
                !argsObj.ContainsKey(methodParams[0].Name))
            {
                argsObj = new JObject { [methodParams[0].Name] = argsObj };
            }

            var paramValues = new object[methodParams.Length];
            for (int i = 0; i < methodParams.Length; i++)
            {
                var p = methodParams[i];
                var node = argsObj[p.Name];

                if (node == null)
                {
                    if (p.HasDefaultValue)
                        paramValues[i] = p.DefaultValue;
                    else if (!p.ParameterType.IsValueType || Nullable.GetUnderlyingType(p.ParameterType) != null)
                        paramValues[i] = null;
                    else
                        throw new ToolValidationException(p.Name, "Missing required parameter.");
                    continue;
                }

                // Validate against schema
                var schema = p.ParameterType.GetSchemaForType();
                var errors = ValidateAgainstSchema(node, schema, p.Name);
                if (errors.Any())
                    throw new ToolValidationAggregateException(errors);

                try
                {
                    paramValues[i] = node.ToObject(p.ParameterType);
                }
                catch (Exception ex)
                {
                    throw new ToolValidationException(p.Name, $"Invalid type for parameter. {ex.Message}");
                }
            }

            return paramValues;
        }

        public List<ToolValidationError> ValidateAgainstSchema(JToken? node, JObject schema, string path = "")
        {
            var errors = new List<ToolValidationError>();

            if (node == null)
            {
                if (schema["required"] is JArray arr && arr.Count > 0)
                    errors.Add(new ToolValidationError(path, path, "Value required but missing.", "missing"));
                return errors;
            }

            var type = schema["type"]?.ToString();
            switch (type)
            {
                case "string":
                    if (node.Type != JTokenType.String)
                        errors.Add(new ToolValidationError(path, path, "Expected string", "type_error"));
                    break;
                case "integer":
                    if (node.Type != JTokenType.Integer)
                        errors.Add(new ToolValidationError(path, path, "Expected integer", "type_error"));
                    break;
                case "number":
                    if (node.Type != JTokenType.Float && node.Type != JTokenType.Integer)
                        errors.Add(new ToolValidationError(path, path, "Expected number", "type_error"));
                    break;
                case "boolean":
                    if (node.Type != JTokenType.Boolean)
                        errors.Add(new ToolValidationError(path, path, "Expected boolean", "type_error"));
                    break;
                case "array":
                    if (node.Type != JTokenType.Array)
                    {
                        errors.Add(new ToolValidationError(path, path, "Expected array", "type_error"));
                    }
                    else if (schema["items"] is JObject itemSchema)
                    {
                        var arrNode = (JArray)node;
                        for (int idx = 0; idx < arrNode.Count; idx++)
                            errors.AddRange(ValidateAgainstSchema(arrNode[idx], itemSchema, $"{path}[{idx}]"));
                    }
                    break;
                case "object":
                    if (node.Type != JTokenType.Object)
                    {
                        errors.Add(new ToolValidationError(path, path, "Expected object", "type_error"));
                    }
                    else if (schema["properties"] is JObject propSchemas)
                    {
                        var objNode = (JObject)node;
                        foreach (var kvp in propSchemas)
                        {
                            var key = kvp.Key;
                            var propSchema = kvp.Value as JObject;
                            if (propSchema == null) continue;

                            if (!objNode.ContainsKey(key))
                            {
                                if (schema["required"] is JArray reqArr && reqArr.Any(r => r?.ToString() == key))
                                    errors.Add(new ToolValidationError(key, $"{path}.{key}".Trim('.'), $"Missing required field '{key}'", "missing"));
                            }
                            else
                            {
                                errors.AddRange(ValidateAgainstSchema(objNode[key], propSchema, $"{path}.{key}".Trim('.')));
                            }
                        }
                    }
                    break;
            }

            return errors;
        }
    }
}

public sealed class ToolValidationError
{
    public string Param { get; }
    public string? Path { get; }
    public string Message { get; }
    public string ErrorType { get; }

    public ToolValidationError(string param, string? path, string message, string errorType)
    {
        Param = param;
        Path = path;
        Message = message;
        ErrorType = errorType;
    }
}
//multiple parameters are wrong at the same time
public sealed class ToolValidationAggregateException : Exception
{
    public IReadOnlyList<ToolValidationError> Errors { get; }

    public ToolValidationAggregateException(IEnumerable<ToolValidationError> errors)
        : base("Tool validation failed") => Errors = errors.ToList();

    public override string ToString()
        => $"Validation failed for the following {Errors.Count} parameters:\n" +
        $" {string.Join(", ", Errors.Select(e => e.ToString()))}";
}
//on param wrong
public sealed class ToolValidationException : Exception
{
    public string ParamName { get; }

    public ToolValidationException(string param, string msg)
        : base($"Validation failed for parameter '{param}'. Details: '{msg}'") => ParamName = param;

    public override string ToString()
        => $"[{ParamName}] => {Message}";
}
