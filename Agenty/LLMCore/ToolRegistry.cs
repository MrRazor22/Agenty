using Agenty.Utils;
using OpenAI.Chat;
using System;
using System.Collections;
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

        public void RegisterAll(params Delegate[] funcs)
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
                else if (paramType.IsGenericType &&
                         paramType.GetGenericTypeDefinition() == typeof(Dictionary<,>) &&
                         paramType.GetGenericArguments()[0] == typeof(string))
                {
                    var valueType = paramType.GetGenericArguments()[1];
                    paramJson["type"] = "object";
                    paramJson["additionalProperties"] = GetTypeSchema(valueType);
                }
                else if (Util.IsSimpleType(paramType))
                {
                    paramJson["type"] = MapClrTypeToJsonType(paramType);
                }
                else
                {
                    paramJson["type"] = "object";
                    var nested = GenerateObjectSchema(param.ParameterType);
                    paramJson["properties"] = nested["properties"]!.Deserialize<JsonObject>();
                    paramJson["required"] = nested["required"]!.Deserialize<JsonArray>();
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
        private JsonObject GenerateObjectSchema(Type type, HashSet<Type>? visited = null)
        {
            visited ??= new HashSet<Type>();
            if (visited.Contains(type))
                return new JsonObject(); // prevent infinite recursion

            visited.Add(type);

            var properties = new JsonObject();
            var required = new JsonArray();

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var propType = prop.PropertyType;
                var propJson = new JsonObject
                {
                    ["description"] = prop.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "No description"
                };

                // Handle Nullable<T>
                var isNullable = Nullable.GetUnderlyingType(propType) != null;
                if (isNullable)
                    propType = Nullable.GetUnderlyingType(propType);

                // Handle EnumValuesAttribute
                var enumAttr = prop.GetCustomAttribute<EnumValuesAttribute>();
                if (enumAttr != null)
                {
                    propJson["type"] = "string";
                    propJson["enum"] = new JsonArray(enumAttr.Values.Select(value => JsonValue.Create(value)).ToArray());
                }
                else if (propType.IsEnum)
                {
                    propJson["type"] = "string";
                    propJson["enum"] = new JsonArray(Enum.GetNames(propType).Select(value => JsonValue.Create(value)).ToArray());
                }
                else if (propType.IsArray)
                {
                    var elementType = propType.GetElementType();
                    propJson["type"] = "array";
                    propJson["items"] = GetTypeSchema(elementType!, visited);
                }
                else if (typeof(IEnumerable).IsAssignableFrom(propType) && propType.IsGenericType)
                {
                    var elementType = propType.GetGenericArguments()[0];
                    propJson["type"] = "array";
                    propJson["items"] = GetTypeSchema(elementType, visited);
                }
                else if (propType.IsGenericType &&
         propType.GetGenericTypeDefinition() == typeof(Dictionary<,>) &&
         propType.GetGenericArguments()[0] == typeof(string))
                {
                    var valueType = propType.GetGenericArguments()[1];
                    propJson["type"] = "object";
                    propJson["additionalProperties"] = GetTypeSchema(valueType, visited);
                }

                else if (Util.IsSimpleType(propType))
                {
                    propJson["type"] = MapClrTypeToJsonType(propType);
                }
                else
                {
                    propJson["type"] = "object";
                    var nested = GenerateObjectSchema(propType, visited);
                    propJson["properties"] = nested["properties"]!.Deserialize<JsonObject>();
                    propJson["required"] = nested["required"]!.Deserialize<JsonArray>();
                }

                properties[prop.Name] = propJson;

                if (!isNullable && !IsOptionalProperty(prop))
                    required.Add(prop.Name);
            }

            return new JsonObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = required
            };
        }

        private JsonObject GetTypeSchema(Type type)
        {
            var visited = new HashSet<Type>();
            return GetTypeSchema(type, visited);
        }

        private JsonObject GetTypeSchema(Type type, HashSet<Type> visited)
        {
            if (Util.IsSimpleType(type))
                return new JsonObject { ["type"] = MapClrTypeToJsonType(type) };

            return GenerateObjectSchema(type, visited);
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
