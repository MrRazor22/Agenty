// Enhancing Tools class to support Enums, Async, Recursive, Overloads, Metadata, etc.
using Agenty.Utils;
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
            typeSchema["description"] = desc;

            ((JsonObject)schema["properties"]!)[name] = typeSchema;
            if (!param.IsOptional) ((JsonArray)schema["required"]!).Add(name);
        }

        return new Tool
        {
            Name = method.Name,
            Description = description,
            Parameters = schema,
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

    public T? Invoke<T>(Tool toolCall)
    {
        if (toolCall == null) throw new ArgumentNullException(nameof(toolCall));

        var tool = _registeredTools.FirstOrDefault(t => t.Name.Equals(toolCall.Name, StringComparison.OrdinalIgnoreCase));
        if (tool?.Function == null) throw new InvalidOperationException($"Tool '{toolCall.Name}' not registered or has no function.");

        var func = tool.Function;
        var method = func.Method;
        var methodParams = method.GetParameters();
        var argsObj = toolCall.Parameters ?? throw new ArgumentException("ToolCallInfo.Parameters is null");

        if (methodParams.Length == 1 && !Util.IsSimpleType(methodParams[0].ParameterType) && !argsObj.ContainsKey(methodParams[0].Name!))
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
                paramValues[i] = JsonSerializer.Deserialize(node.ToJsonString(), p.ParameterType, new JsonSerializerOptions
                {
                    Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
                    PropertyNameCaseInsensitive = true
                });
            }
        }

        var result = func.DynamicInvoke(paramValues);
        if (result == null) return default;
        if (result is T typedResult) return typedResult;
        throw new InvalidCastException($"Expected result of type {typeof(T).Name}, got {result.GetType().Name}.");
    }

    private static bool IsOptional(PropertyInfo prop)
    {
        var type = prop.PropertyType;
        return Nullable.GetUnderlyingType(type) != null || (type.IsClass && type != typeof(string));
    }

    private static string? MapClrTypeToJsonType(Type type)
    {
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
                    JsonNode.Parse(t.Parameters?.ToJsonString() ?? "{}")!.AsObject()
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