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

public class ToolManager(IEnumerable<Tool>? tools = null) : ITools
{
    private List<Tool> _registeredTools = tools?.ToList() ?? new();
    public IReadOnlyList<Tool> RegisteredTools => _registeredTools;

    public static implicit operator ToolManager(List<Tool> tools) => new ToolManager(tools);
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
            SchemaDefinition = schema,
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

    public override string ToString() =>
        string.Join("\n", RegisteredTools.Select(t => t.ToString()));
}