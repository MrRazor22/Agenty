using Agenty.Utils;
using OpenAI.Chat;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Agenty.LLMCore;

public class ToolRegistry : IToolRegistry
{
    private readonly List<Tool> _registeredTools = new();

    public void RegisterAll(List<Delegate> funcs) => funcs.ForEach(f => Register(f));
    public void RegisterAll(params Delegate[] funcs) => funcs.ToList().ForEach(f => Register(f));

    public void Register(Delegate func, params string[] tags)
    {
        var tool = CreateToolFromDelegate(func);
        if (tags?.Length > 0)
            tool.Tags.AddRange(tags);
        _registeredTools.Add(tool);
    }
    public void RegisterAllFromType(Type type)
    {
        var methods = type
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.GetCustomAttribute<DescriptionAttribute>() != null);

        foreach (var method in methods)
        {
            var del = Delegate.CreateDelegate(
                Expression.GetDelegateType(
                    method.GetParameters()
                        .Select(p => p.ParameterType)
                        .Concat(new[] { method.ReturnType })
                        .ToArray()), method);

            Register(del);
        }
    }

    public List<Tool> GetRegisteredTools() => _registeredTools.ToList();

    public List<Tool> GetAllTools() => _registeredTools;

    public List<Tool> GetToolsByTag(string tag) =>
        _registeredTools.Where(t => t.Tags.Contains(tag)).ToList();


    public Tool CreateToolFromDelegate(Delegate func)
    {
        var method = func.Method;
        var description = method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "No description";
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
            var desc = param.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "No description";
            var typeSchema = GetSchemaForType(param.ParameterType);
            typeSchema["description"] = desc;

            ((JsonObject)schema["properties"]!)[name] = typeSchema;
            if (!param.IsOptional) ((JsonArray)schema["required"]!).Add(name);
        }

        return new Tool
        {
            Name = method.Name,
            Description = description,
            ParameterSchema = schema,
            Function = func
        };
    }

    private JsonObject GetSchemaForType(Type type, HashSet<Type>? visited = null)
    {
        visited ??= new HashSet<Type>();
        type = Nullable.GetUnderlyingType(type) ?? type;

        if (Util.IsSimpleType(type))
            return new JsonObject { ["type"] = MapClrTypeToJsonType(type) };

        if (type.IsEnum)
            return new JsonObject
            {
                ["type"] = "string",
                ["enum"] = new JsonArray(Enum.GetNames(type).Select(value => JsonValue.Create(value)).ToArray())
            };

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
            propSchema["description"] = prop.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "No description";

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
        if (type == typeof(string)) return "string";
        if (type == typeof(bool)) return "boolean";
        if (type == typeof(int) || type == typeof(long)) return "integer";
        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal)) return "number";
        return "object";
    }
}
