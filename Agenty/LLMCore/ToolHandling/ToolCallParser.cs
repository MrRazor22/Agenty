using Agenty.AgentCore.Runtime;
using Agenty.LLMCore.JsonSchema;
using Agenty.LLMCore.Messages;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Agenty.LLMCore.ToolHandling
{
    public interface IToolCallParser
    {
        ToolCallResponse TryExtractInlineToolCall(IToolRegistry registry, string content, bool strict = false);
        object?[] ParseToolParams(IToolRegistry registry, string toolName, JsonObject arguments);
        List<ToolValidationError> ValidateAgainstSchema(JsonNode? node, JsonObject schema, string path = "");
    }

    public sealed class ToolCallParser : IToolCallParser
    {
        private const string ToolJsonNameTag = "name";
        private const string ToolJsonArgumentsTag = "arguments";
        private const string ToolJsonAssistantMessageTag = "message";

        private readonly JsonSerializerOptions JsonOptions = new()
        {
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
            PropertyNameCaseInsensitive = true
        };

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

        public ToolCallResponse TryExtractInlineToolCall(IToolRegistry registry, string content, bool strict = false)
        {
            var matches = ToolTagPattern.Matches(content).Cast<Match>()
                .Concat(LooseToolJsonPattern.Matches(content).Cast<Match>())
                .Concat(MessageOnlyPattern.Matches(content).Cast<Match>())
                .OrderBy(m => m.Index)
                .ToList();

            string fallback = matches.Any() ? content[..matches[0].Index].Trim() : content.Trim();
            var toolCalls = new List<ToolCall>();
            string? assistantMessage = null;

            foreach (var match in matches)
            {
                string jsonStr = match.Groups["json"].Value.Trim();
                JsonObject? node = null;
                try { node = JsonNode.Parse(jsonStr)?.AsObject(); }
                catch
                {
                    if (strict)
                        return new ToolCallResponse(Array.Empty<ToolCall>(), $"Invalid JSON: `{jsonStr}`", null);
                    continue;
                }
                if (node == null) continue;

                var hasName = node.ContainsKey(ToolJsonNameTag);
                var hasArgs = node.ContainsKey(ToolJsonArgumentsTag);
                var hasMessage = node.ContainsKey(ToolJsonAssistantMessageTag);

                if (hasName && hasArgs)
                {
                    var name = node[ToolJsonNameTag]!.ToString();
                    var args = node[ToolJsonArgumentsTag] is JsonObject argObj
                        ? JsonNode.Parse(argObj.ToJsonString())!.AsObject()
                        : new JsonObject();
                    var message = node[ToolJsonAssistantMessageTag]?.ToString();

                    if (string.IsNullOrEmpty(name))
                        return new ToolCallResponse(Array.Empty<ToolCall>(), "Tool call missing 'name'.", null);

                    if (!registry.Contains(name))
                        return new ToolCallResponse(Array.Empty<ToolCall>(),
                            $"Tool `{name}` not registered. Available: {string.Join(", ", registry.RegisteredTools.Select(t => t.Name))}",
                            null);

                    var id = node.ContainsKey("id") ? node["id"]?.ToString() ?? Guid.NewGuid().ToString() : Guid.NewGuid().ToString();
                    toolCalls.Add(new ToolCall(id, name, args, ParseToolParams(registry, name, args), message));
                    continue;
                }

                if (!hasName && !hasArgs && hasMessage)
                {
                    toolCalls.Add(new(node[ToolJsonAssistantMessageTag]?.ToString() ?? fallback));
                    return new ToolCallResponse(toolCalls, assistantMessage, null);
                }

                if (strict)
                {
                    if (!hasName && hasArgs)
                        toolCalls.Add(new("Tool call has arguments but no 'name'."));
                    if (hasName && !hasArgs)
                        toolCalls.Add(new($"Tool `{node[ToolJsonNameTag]}` missing 'arguments'."));
                }
            }

            if (toolCalls.Count == 0 && strict)
                toolCalls.Add(new("No valid tool call structure found."));

            if (!string.IsNullOrWhiteSpace(fallback))
                assistantMessage = fallback;

            return new ToolCallResponse(toolCalls, assistantMessage, null);
        }
        public object?[] ParseToolParams(IToolRegistry registry, string toolName, JsonObject arguments)
        {
            var tool = registry.Get(toolName);
            if (tool?.Function == null)
                throw new InvalidOperationException($"Tool '{toolName}' not registered or has no function.");

            var method = tool.Function.Method;
            var methodParams = method.GetParameters();
            var argsObj = arguments ?? throw new ArgumentException("Arguments null");

            // wrap single complex param if needed
            if (methodParams.Length == 1 &&
                !methodParams[0].ParameterType.IsSimpleType() &&
                !argsObj.ContainsKey(methodParams[0].Name!))
            {
                argsObj = new JsonObject { [methodParams[0].Name!] = argsObj };
            }

            var paramValues = new object?[methodParams.Length];
            for (int i = 0; i < methodParams.Length; i++)
            {
                var p = methodParams[i];

                // check required
                if (!argsObj.TryGetPropertyValue(p.Name!, out var node) || node == null)
                {
                    if (p.HasDefaultValue)
                        paramValues[i] = p.DefaultValue;
                    else if (!p.ParameterType.IsValueType || Nullable.GetUnderlyingType(p.ParameterType) != null)
                        paramValues[i] = null;
                    else
                        throw new ToolValidationException(p.Name!, "Missing required parameter.");
                    continue;
                }

                // validate against schema
                var schema = p.ParameterType.GetSchemaForType();
                var errors = ValidateAgainstSchema(node, schema, p.Name!);
                if (errors.Any())
                    throw new ToolValidationAggregateException(errors);

                // try deserialization (with coercion attempt)
                try
                {
                    paramValues[i] = JsonSerializer.Deserialize(node.ToJsonString(), p.ParameterType, JsonOptions);
                }
                catch (Exception ex)
                {
                    throw new ToolValidationException(p.Name!, $"Invalid type for parameter. {ex.Message}");
                }
            }

            return paramValues;
        }

        // === new helper ===
        public List<ToolValidationError> ValidateAgainstSchema(JsonNode? node, JsonObject schema, string path = "")
        {
            var errors = new List<ToolValidationError>();

            if (node == null)
            {
                if (schema.TryGetPropertyValue("required", out var req) && req is JsonArray arr && arr.Count > 0)
                {
                    errors.Add(new ToolValidationError(path, path, "Value required but missing.", "missing"));
                }
                return errors;
            }

            if (schema.TryGetPropertyValue("type", out var typeNode))
            {
                var type = typeNode?.ToString();
                switch (type)
                {
                    case "string":
                        if (node is not JsonValue sVal || sVal.GetValue<string>() == null)
                            errors.Add(new ToolValidationError(path, path, "Expected string", "type_error"));
                        break;

                    case "integer":
                        if (node is not JsonValue iVal || !int.TryParse(iVal.ToString(), out _))
                            errors.Add(new ToolValidationError(path, path, "Expected integer", "type_error"));
                        break;

                    case "number":
                        if (node is not JsonValue nVal || !double.TryParse(nVal.ToString(), out _))
                            errors.Add(new ToolValidationError(path, path, "Expected number", "type_error"));
                        break;

                    case "boolean":
                        if (node is not JsonValue bVal || !bool.TryParse(bVal.ToString(), out _))
                            errors.Add(new ToolValidationError(path, path, "Expected boolean", "type_error"));
                        break;

                    case "array":
                        if (node is not JsonArray arrNode)
                        {
                            errors.Add(new ToolValidationError(path, path, "Expected array", "type_error"));
                        }
                        else if (schema.TryGetPropertyValue("items", out var itemsSchema) && itemsSchema is JsonObject itemSchema)
                        {
                            for (int idx = 0; idx < arrNode.Count; idx++)
                            {
                                errors.AddRange(ValidateAgainstSchema(arrNode[idx], itemSchema, $"{path}[{idx}]"));
                            }
                        }
                        break;

                    case "object":
                        if (node is not JsonObject objNode)
                        {
                            errors.Add(new ToolValidationError(path, path, "Expected object", "type_error"));
                        }
                        else if (schema.TryGetPropertyValue("properties", out var props) && props is JsonObject propSchemas)
                        {
                            foreach (var kvp in propSchemas)
                            {
                                var key = kvp.Key;
                                var propSchema = kvp.Value?.AsObject();
                                if (propSchema == null) continue;

                                if (!objNode.TryGetPropertyValue(key, out var child))
                                {
                                    if (schema.TryGetPropertyValue("required", out var reqNode) && reqNode is JsonArray reqArr && reqArr.Any(r => r?.ToString() == key))
                                    {
                                        errors.Add(new ToolValidationError(key, $"{path}.{key}".Trim('.'), $"Missing required field '{key}'", "missing"));
                                    }
                                }
                                else
                                {
                                    errors.AddRange(ValidateAgainstSchema(child, propSchema, $"{path}.{key}".Trim('.')));
                                }
                            }
                        }
                        break;
                }
            }

            // constraints
            if (schema.TryGetPropertyValue("minLength", out var minNode) && node?.ToString()?.Length < minNode!.GetValue<int>())
                errors.Add(new ToolValidationError(path, path, $"String shorter than {minNode}", "too_short"));

            if (schema.TryGetPropertyValue("maxLength", out var maxNode) && node?.ToString()?.Length > maxNode!.GetValue<int>())
                errors.Add(new ToolValidationError(path, path, $"String longer than {maxNode}", "too_long"));

            if (schema.TryGetPropertyValue("pattern", out var patternNode) && !Regex.IsMatch(node?.ToString() ?? "", patternNode!.ToString()))
                errors.Add(new ToolValidationError(path, path, $"Value does not match regex {patternNode}", "pattern_mismatch"));

            if (schema.TryGetPropertyValue("enum", out var enumNode) && enumNode is JsonArray enumArr && !enumArr.Any(e => e?.ToString() == node?.ToString()))
                errors.Add(new ToolValidationError(path, path, $"Value '{node}' not in allowed enum", "enum_mismatch"));

            return errors;
        }
    }
    public sealed record ToolValidationError(
    string Param,
    string? Path,
    string Message,
    string ErrorType    
    );

    public sealed class ToolValidationAggregateException : Exception
    {
        public IReadOnlyList<ToolValidationError> Errors { get; }
        public ToolValidationAggregateException(IEnumerable<ToolValidationError> errors)
            : base("Tool validation failed") => Errors = errors.ToList();
    }

    public sealed class ToolValidationException : Exception
    {
        public string ParamName { get; }
        public ToolValidationException(string param, string msg)
            : base($"Validation failed for '{param}': {msg}") => ParamName = param;
    }

}
