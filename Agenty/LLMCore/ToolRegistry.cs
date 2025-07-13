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
    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
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

            var funcParam = method.GetParameters();

            JsonObject schema = ExtractParameterJson(funcParam);


            var tool = new Tool
            {
                Name = method.Name,
                Description = funcDescription,
                ParameterSchema = schema,
                Function = func
            };
            return tool;
        }

        private JsonObject ExtractParameterJson(ParameterInfo[] parameters)
        {
            var properties = new JsonObject();
            var required = new JsonArray();

            foreach (var param in parameters)
            {
                var paramDesc = param.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "No description";

                JsonObject paramJson = new JsonObject
                {
                    ["description"] = paramDesc
                };

                var paramType = param.ParameterType;

                if (paramType.IsArray)
                {
                    var elementType = paramType.GetElementType();
                    var itemType = MapClrTypeToJsonType(elementType!);
                    paramJson["type"] = "array";
                    paramJson["items"] = new JsonObject { ["type"] = itemType };
                }
                else if (IsSimpleType(paramType))
                {
                    paramJson["type"] = MapClrTypeToJsonType(paramType);
                }
                else
                {
                    var nestedSchema = GenerateObjectSchema(paramType);
                    paramJson["type"] = "object";
                    paramJson["properties"] = nestedSchema["properties"]!.DeepClone();
                    paramJson["required"] = nestedSchema["required"]!.DeepClone();
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
                ["type"] = "object", //this obj wrap all parameters
                ["properties"] = properties,
                ["required"] = required
            };
            return schema;
        }
        private JsonObject GenerateObjectSchema(Type type)
        {
            var properties = new JsonObject();
            var required = new JsonArray();

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var propType = prop.PropertyType;

                var propJson = new JsonObject
                {
                    ["description"] = prop.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "No description"
                };

                if (propType.IsArray)
                {
                    var elementType = propType.GetElementType();
                    propJson["type"] = "array";
                    propJson["items"] = new JsonObject { ["type"] = MapClrTypeToJsonType(elementType!) };
                }
                else if (IsSimpleType(propType))
                {
                    propJson["type"] = MapClrTypeToJsonType(propType);
                }
                else
                {
                    var nested = GenerateObjectSchema(propType);
                    propJson["type"] = "object";
                    var cloned = nested.DeepClone() as JsonObject;
                    propJson["properties"] = cloned!["properties"]!.Deserialize<JsonObject>();
                    propJson["required"] = cloned!["required"]!.Deserialize<JsonArray>();
                }

                var enumAttr = prop.GetCustomAttribute<EnumValuesAttribute>();
                if (enumAttr != null)
                    propJson["enum"] = new JsonArray(enumAttr.Values.Select(value => JsonValue.Create(value)).ToArray());

                properties[prop.Name!] = propJson;

                if (!IsOptionalProperty(prop))
                    required.Add(prop.Name!);
            }

            return new JsonObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = required
            };
        }
        private bool IsSimpleType(Type type)
        {
            return type.IsPrimitive || type == typeof(string) || type == typeof(decimal) || type == typeof(DateTime) || type.IsEnum;
        }
        private bool IsOptionalProperty(PropertyInfo prop)
        {
            var type = prop.PropertyType;
            return Nullable.GetUnderlyingType(type) != null ||
                   (type.IsClass && type != typeof(string)); // treat ref types (except string) as optional
        }

        private string? MapClrTypeToJsonType(Type type)
        {
            if (type == typeof(string)) return "string";
            if (type == typeof(int) || type == typeof(long)) return "integer";
            if (type == typeof(float) || type == typeof(double) || type == typeof(decimal)) return "number";
            if (type == typeof(bool)) return "boolean";
            if (type.IsArray || typeof(System.Collections.IEnumerable).IsAssignableFrom(type)) return "array";
            return "object"; // unsupported or complex types
        }
    }
}
