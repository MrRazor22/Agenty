using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Agenty.Utilities
{
    public static class JsonSchemaExtensions
    {
        private const string TypeKey = "type";
        private const string PropertiesKey = "properties";
        private const string RequiredKey = "required";

        /// <summary>
        /// Updates the JsonObject to be a standard JSON schema with type, properties, and required arrays.
        /// </summary>
        /// <param name="schema">The JsonObject to update (can be new or existing).</param>
        /// <param name="properties">The properties object of the schema.</param>
        /// <param name="required">The array of required property names.</param>
        /// <param name="type">The JSON schema type (default: "object").</param>
        /// <returns>The updated JsonObject (same instance as input).</returns>
        public static JsonObject UpdateStandardTypeSchema(
            this JsonObject schema,
            JsonObject properties,
            JsonArray required,
            string? type = null)
        {
            schema[TypeKey] = type ?? "object";
            schema[PropertiesKey] = properties;
            schema[RequiredKey] = required;
            return schema;
        }

        public static JsonObject GetSchemaForType(this Type type, HashSet<Type>? visited = null)
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

            if (type.IsSimpleType())
                return new JsonObject { ["type"] = type.MapClrTypeToJsonType() };

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

            if (visited.Contains(type)) return new JsonObject(); ;
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

            return new JsonObject().UpdateStandardTypeSchema(props, required);
        }

        private static bool IsOptional(this PropertyInfo prop)
        {
            var type = prop.PropertyType;
            return Nullable.GetUnderlyingType(type) != null || (type.IsClass && type != typeof(string));
        }
        public static bool IsSimpleType(this Type type)
        {
            return type.IsPrimitive || type == typeof(string) || type == typeof(decimal) || type == typeof(DateTime);
        }

        public static string MapClrTypeToJsonType(this Type type)
        {
            if (type == typeof(Enum)) return "Enum";
            if (type == typeof(string)) return "string";
            if (type == typeof(bool)) return "boolean";
            if (type == typeof(int) || type == typeof(long)) return "integer";
            if (type == typeof(float) || type == typeof(double) || type == typeof(decimal)) return "number";
            return "object";
        }
    }
}
