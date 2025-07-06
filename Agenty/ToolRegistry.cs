using OpenAI.Chat;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace Agenty
{
    [AttributeUsage(AttributeTargets.Parameter)]
    public class EnumValuesAttribute : Attribute
    {
        public string[] Values { get; }
        public EnumValuesAttribute(params string[] values) => Values = values;
    }

    public class ToolRegistry
    {
        private readonly Dictionary<string, Delegate> _toolMap = new();
        public ChatTool RegisterTool(Delegate func)
        {
            var method = func.Method;

            // Store the delegate for later execution
            _toolMap[method.Name.ToLowerInvariant()] = func;

            var funcDescription = method
                .GetCustomAttribute<DescriptionAttribute>()?.Description ?? "No description";

            var properties = new JsonObject();
            var required = new JsonArray();

            foreach (var param in method.GetParameters())
            {
                var paramDesc = param.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "No description";

                JsonObject paramJson = new JsonObject
                {
                    ["description"] = paramDesc
                };

                if (param.ParameterType.IsArray)
                {
                    var elementType = param.ParameterType.GetElementType();
                    var itemType = MapClrTypeToJsonType(elementType!);
                    paramJson["type"] = "array";
                    paramJson["items"] = new JsonObject { ["type"] = itemType };
                }
                else
                {
                    var typeStr = MapClrTypeToJsonType(param.ParameterType);
                    paramJson["type"] = typeStr;
                }

                var enumAttr = param.GetCustomAttribute<EnumValuesAttribute>();
                if (enumAttr != null)
                    paramJson["enum"] = new JsonArray(enumAttr.Values.Select(value => JsonValue.Create(value)).ToArray());

                properties[param.Name!] = paramJson;


                if (!param.IsOptional)
                    required.Add(param.Name!);
            }

            var schema = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = required
            };

            return ChatTool.CreateFunctionTool(
                functionName: method.Name,
                functionDescription: funcDescription,
                functionParameters: BinaryData.FromString(schema.ToJsonString())
            );
        }

        public string? InvokeTool(string name, string argumentsJson)
        {
            if (!_toolMap.TryGetValue(name.ToLowerInvariant(), out var func))
                return $"Tool '{name}' not registered.";

            var method = func.Method;
            var methodParams = method.GetParameters();

            var argsObj = JsonNode.Parse(argumentsJson)?.AsObject();
            if (argsObj == null)
                return "[Invalid JSON arguments]";

            var paramValues = new object?[methodParams.Length];
            for (int i = 0; i < methodParams.Length; i++)
            {
                var p = methodParams[i];
                if (!argsObj.TryGetPropertyValue(p.Name!, out var node) || node == null)
                    paramValues[i] = Type.Missing; // fallback for optional
                else
                    paramValues[i] = JsonSerializer.Deserialize(node.ToJsonString(), p.ParameterType);
            }

            var result = func.DynamicInvoke(paramValues);
            return result?.ToString();
        }

        private string? MapClrTypeToJsonType(Type type)
        {
            if (type == typeof(string)) return "string";
            if (type == typeof(int) || type == typeof(long)) return "integer";
            if (type == typeof(float) || type == typeof(double) || type == typeof(decimal)) return "number";
            if (type == typeof(bool)) return "boolean";
            if (type.IsArray || typeof(System.Collections.IEnumerable).IsAssignableFrom(type)) return "array";
            return null; // unsupported or complex types
        }
    }
}
