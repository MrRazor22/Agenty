// Enhancing Tools class to support Enums, Async, Recursive, Overloads, Metadata, etc.
using Agenty.Utilities;
using Microsoft.Win32;
using System.Collections;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Agenty.LLMCore;

public class Tools(IEnumerable<Tool>? tools = null) : ITools
{
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
            (?<json>\{{[\s\S]*\}})         # JSON object
            \s*                           # optional whitespace/newlines
            (?<close>{tagPattern})        # closing tag like [END_TOOL_REQUEST]
        ";

    static readonly string looseToolJsonPattern = @"
                    (?<json>
                        \{
                          \s*""name""\s*:\s*""[^""]+""
                          \s*,\s*
                          ""arguments""\s*:\s*\{[\s\S]*\}
                          \s*
                        \}
                    )
                ";
    #endregion

    private List<Tool> _registeredTools = tools?.ToList() ?? new();
    IReadOnlyList<Tool> ITools.RegisteredTools => _registeredTools;
    public IEnumerator<Tool> GetEnumerator() => _registeredTools.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public static implicit operator Tools(List<Tool> tools) => new Tools(tools);

    public void Register(params Delegate[] funcs)
    {
        foreach (var f in funcs)
        {
            var tool = CreateToolFromDelegate(f);
            _registeredTools.Add(tool);
        }
    }
    public void RegisterAll(Type type)
    {
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.GetCustomAttribute<DescriptionAttribute>() != null);

        foreach (var method in methods)
        {
            try
            {
                var del = Delegate.CreateDelegate(
                    Expression.GetDelegateType(
                        method.GetParameters().Select(p => p.ParameterType)
                        .Concat(new[] { method.ReturnType })
                        .ToArray()), method);

                Register(del);
            }
            catch
            {
                // Skip overloads or mismatches
            }
        }
    }
    public Tool? Get(Delegate func)
    {
        if (func == null) throw new ArgumentNullException(nameof(func));
        var method = func.Method;
        return _registeredTools.FirstOrDefault(t => t.Function?.Method == method);
    }
    public bool Contains(string toolName) => _registeredTools.Any(t =>
            t.Name.Equals(toolName, StringComparison.OrdinalIgnoreCase));
    public Tool? Get(string toolName)
        => _registeredTools.FirstOrDefault(t => t.Name.Equals(toolName, StringComparison.InvariantCultureIgnoreCase));
    private Tool CreateToolFromDelegate(Delegate func)
    {
        var method = func.Method;
        var description = method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? method.Name;
        var parameters = method.GetParameters();

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject(),
            ["required"] = new JsonArray()
        };

        foreach (var param in parameters)
        {
            var name = param.Name!;
            var desc = param.GetCustomAttribute<DescriptionAttribute>()?.Description ?? name;
            var typeSchema = GetSchemaForType(param.ParameterType);
            typeSchema["description"] ??= desc;

            ((JsonObject)schema["properties"]!)[name] = typeSchema;
            if (!param.IsOptional) ((JsonArray)schema["required"]!).Add(name);
        }

        return new Tool
        {
            Name = method.Name,
            Description = description,
            ArgumentSchema = schema,
            Function = func
        };
    }

    private JsonObject GetSchemaForType(Type type, HashSet<Type>? visited = null)
    {
        visited ??= new HashSet<Type>();
        type = Nullable.GetUnderlyingType(type) ?? type;

        if (type.IsEnum)
            return new JsonObject
            {
                ["type"] = "string",
                ["enum"] = new JsonArray(Enum.GetNames(type).Select((e) => JsonValue.Create(e)).ToArray()),
                ["description"] = $"One of: {string.Join(", ", Enum.GetNames(type))}"
            };

        if (Util.IsSimpleType(type))
            return new JsonObject { ["type"] = MapClrTypeToJsonType(type) };

        if (type.IsArray)
            return new JsonObject { ["type"] = "array", ["items"] = GetSchemaForType(type.GetElementType()!, visited) };

        if (typeof(IEnumerable).IsAssignableFrom(type) && type.IsGenericType)
            return new JsonObject { ["type"] = "array", ["items"] = GetSchemaForType(type.GetGenericArguments()[0], visited) };

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>) &&
            type.GetGenericArguments()[0] == typeof(string))
        {
            var valueType = type.GetGenericArguments()[1];
            return new JsonObject { ["type"] = "object", ["additionalProperties"] = GetSchemaForType(valueType, visited) };
        }

        if (visited.Contains(type)) return new JsonObject();
        visited.Add(type);

        var props = new JsonObject();
        var required = new JsonArray();

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var propType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
            var propSchema = GetSchemaForType(propType, visited);
            propSchema["description"] = prop.GetCustomAttribute<DescriptionAttribute>()?.Description ?? prop.Name;

            if (prop.GetCustomAttribute<EmailAddressAttribute>() != null)
                propSchema["format"] = "email";
            if (prop.GetCustomAttribute<StringLengthAttribute>() is { } len)
            {
                propSchema["minLength"] = len.MinimumLength;
                propSchema["maxLength"] = len.MaximumLength;
            }
            if (prop.GetCustomAttribute<RegularExpressionAttribute>() is { } regex)
                propSchema["pattern"] = regex.Pattern;

            props[prop.Name] = propSchema;
            if (!IsOptional(prop)) required.Add(prop.Name);
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = props,
            ["required"] = required
        };
    }

    private bool IsToolSchemaMatch(JsonObject input, JsonObject schema)
    {
        var inputKeys = input.Select(p => p.Key).ToHashSet();
        var schemaKeys = schema["properties"]?.AsObject()?.Select(p => p.Key).ToHashSet() ?? new();
        return schemaKeys.SetEquals(inputKeys);
    }

    public object?[] ParseToolParams(string toolName, JsonObject arguments)
    {
        var tool = Get(toolName);
        if (tool?.Function == null)
            throw new InvalidOperationException($"Tool '{toolName}' not registered or has no function.");

        var func = tool.Function!;
        var method = func.Method;
        var methodParams = method.GetParameters();
        var argsObj = arguments ?? throw new ArgumentException("ToolCallInfo.Parameters is null");

        // Handle case where parameters are passed as a single wrapped object
        if (methodParams.Length == 1 && !Util.IsSimpleType(methodParams[0].ParameterType) &&
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



    public Tool? TryExtractInlineToolCall(string content)
    {
        var opts = RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace;

        var match = Regex.Matches(content, toolTagPattern, opts)
             .Cast<Match>()
             .Concat(Regex.Matches(content, looseToolJsonPattern, opts).Cast<Match>())
             .OrderBy(m => m.Index)
             .FirstOrDefault();

        // 3. If nothing matched at all, bail
        if (match == null)
            return null;


        // strip out JUST the JSON (and any tags) from the original content
        var cleaned = content.Substring(0, match.Index).Trim();

        // 4. Extract the JSON and try to parse it
        var jsonStr = match.Groups["json"].Value.Trim();

        JsonObject? node = null;
        try
        {
            node = JsonNode.Parse(jsonStr)?.AsObject();
        }
        catch { /* invalid JSON */ }

        if (node != null &&
            node.ContainsKey("name") &&
            node.ContainsKey("arguments") &&
            Contains(node["name"]!.ToString()))
        {
            var name = node["name"]!.ToString();
            var args = node["arguments"] as JsonObject ?? new JsonObject();


            var tool = Get(name);
            tool!.Id = Guid.NewGuid().ToString();
            tool.Parameters = ParseToolParams(name, args);
            tool.AssistantMessage = cleaned;

            return tool;
        }
        return new Tool
        {
            AssistantMessage = cleaned
        };

    }

    public object?[] ParseToolParameters(Tool toolCall)
    {
        var tool = _registeredTools.FirstOrDefault(t => t.Name.Equals(toolCall.Name, StringComparison.OrdinalIgnoreCase));
        if (tool?.Function == null)
            throw new InvalidOperationException($"Tool '{toolCall.Name}' not registered or has no function.");

        var func = tool.Function!;
        var method = func.Method;
        var methodParams = method.GetParameters();
        var argsObj = toolCall.ArgumentSchema ?? throw new ArgumentException("ToolCallInfo.Parameters is null");

        // Handle case where parameters are passed as a single wrapped object
        if (methodParams.Length == 1 && !Util.IsSimpleType(methodParams[0].ParameterType) &&
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
                else if (!p.ParameterType.IsValueType || Nullable.GetUnderlyingType(p.ParameterType) != null)
                    paramValues[i] = null;
                else
                    throw new ArgumentException($"Missing required parameter '{p.Name}' with no default value.");
            }
            else
            {
                paramValues[i] = JsonSerializer.Deserialize(node.ToJsonString(), p.ParameterType, new JsonSerializerOptions
                {
                    Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
                    PropertyNameCaseInsensitive = true
                });
            }
        }

        return paramValues;
    }

    public async Task<T?> Invoke<T>(Tool tool)
    {
        if (tool == null) throw new ArgumentNullException(nameof(tool));
        var paramValues = tool.Parameters;

        var func = Get(tool.Name)?.Function!;
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



    private static bool IsOptional(PropertyInfo prop)
    {
        var type = prop.PropertyType;
        return Nullable.GetUnderlyingType(type) != null || (type.IsClass && type != typeof(string));
    }

    private static string? MapClrTypeToJsonType(Type type)
    {
        if (type == typeof(Enum)) return "Enum";
        if (type == typeof(string)) return "string";
        if (type == typeof(bool)) return "boolean";
        if (type == typeof(int) || type == typeof(long)) return "integer";
        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal)) return "number";
        return "object";
    }

    /// <summary>
    /// The "enum" ensures the name is valid.
    /// The "oneOf" ensures arguments matches exactly one tool schema.
    /// </summary>
    /// <returns>Returns registered tool schema format</returns>
    public JsonObject GetToolsSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["name"] = new JsonObject
            {
                ["type"] = "string",
                ["enum"] = new JsonArray(
                _registeredTools.Select(t => JsonValue.Create(t.Name)).ToArray()
            )
            },
            ["arguments"] = new JsonObject
            {
                ["type"] = "object",
                ["oneOf"] = new JsonArray(
                _registeredTools.Select(t =>
                    // THIS is the deep clone (we get json string of each paramater and get json node basically cloning explicly) to avoid parent reuse error
                    JsonNode.Parse(t.ArgumentSchema?.ToJsonString() ?? "{}")!.AsObject()
                ).ToArray()
            )
            },
            ["message"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Optional message from the assistant to show before or along with tool call"
            }
        },
        ["required"] = new JsonArray { "name", "arguments", "message" }
    };
}