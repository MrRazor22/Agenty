using OpenAI.Chat;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace Agenty.LLMCore
{
    [AttributeUsage(AttributeTargets.Parameter)]
    public class EnumValuesAttribute : Attribute
    {
        public string[] Values { get; }
        public EnumValuesAttribute(params string[] values) => Values = values;
    }

    public class ToolRegistry : IToolRegistry
    {
        List<Tool> _registeredTools = new();

        public void Register(Delegate func, params string[] tags)
        {
            var tool = CreateToolFromDelegate(func);
            if (tags != null && tags.Length > 0)
                tool.Tags.AddRange(tags);
            _registeredTools.Add(tool);
        }

        public void RegisterAll(List<Delegate> funcs)
        {
            foreach (var f in funcs)
                Register(f);
        }

        public List<Tool> GetRegisteredTools() => _registeredTools;

        public List<Tool> GetToolsByTag(string tag) =>
                 _registeredTools.Where(t => t.Tags.Contains(tag)).ToList();

        public Tool CreateToolFromDelegate(Delegate func)
        {
            var method = func.Method;

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

            var tool = new Tool
            {
                Name = method.Name,
                Description = funcDescription,
                ParameterSchema = schema,
                Function = func
            };
            return tool;
        }

        private string? MapClrTypeToJsonType(Type type)
        {
            if (type == typeof(string)) return "string";
            if (type == typeof(int) || type == typeof(long)) return "integer";
            if (type == typeof(float) || type == typeof(double) || type == typeof(decimal)) return "number";
            if (type == typeof(bool)) return "boolean";
            if (type.IsArray || typeof(System.Collections.IEnumerable).IsAssignableFrom(type)) return "array";
            return "Unknown"; // unsupported or complex types
        }
    }
}
