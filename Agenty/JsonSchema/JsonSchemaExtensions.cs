using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Agenty.JsonSchema
{
    public static class JsonSchemaExtensions
    {
        public static JsonObject GetSchemaForType(this Type type, HashSet<Type>? visited = null)
        {
            visited ??= new HashSet<Type>();
            type = Nullable.GetUnderlyingType(type) ?? type;

            if (type.IsEnum)
                return new JsonSchemaBuilder()
                    .Type("string")
                    .Enum(Enum.GetNames(type))
                    .Description($"One of: {string.Join(", ", Enum.GetNames(type))}")
                    .Build();

            if (type.IsSimpleType())
                return new JsonSchemaBuilder()
                    .Type(type.MapClrTypeToJsonType() ?? "object")
                    .Build();

            if (type.IsArray)
                return new JsonSchemaBuilder()
                    .Type("array")
                    .Items(type.GetElementType()!.GetSchemaForType(visited))
                    .Build();

            if (typeof(IEnumerable).IsAssignableFrom(type) && type.IsGenericType)
                return new JsonSchemaBuilder()
                    .Type("array")
                    .Items(type.GetGenericArguments()[0].GetSchemaForType(visited))
                    .Build();

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>) &&
                type.GetGenericArguments()[0] == typeof(string))
            {
                var valueType = type.GetGenericArguments()[1];
                return new JsonSchemaBuilder()
                    .Type("object")
                    .AdditionalProperties(valueType.GetSchemaForType(visited))
                    .Build();
            }

            if (visited.Contains(type)) return new JsonSchemaBuilder().Build();
            visited.Add(type);

            var props = new JsonObject();
            var required = new JsonArray();

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var propType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                var propSchema = propType.GetSchemaForType(visited);

                var description = prop.GetCustomAttribute<DescriptionAttribute>()?.Description ?? prop.Name;
                propSchema[JsonSchemaConstants.DescriptionKey] = description;

                if (prop.GetCustomAttribute<EmailAddressAttribute>() != null)
                    propSchema[JsonSchemaConstants.FormatKey] = "email";

                if (prop.GetCustomAttribute<StringLengthAttribute>() is { } len)
                {
                    propSchema[JsonSchemaConstants.MinLengthKey] = len.MinimumLength;
                    propSchema[JsonSchemaConstants.MaxLengthKey] = len.MaximumLength;
                }

                if (prop.GetCustomAttribute<RegularExpressionAttribute>() is { } regex)
                    propSchema[JsonSchemaConstants.PatternKey] = regex.Pattern;

                props[prop.Name] = propSchema;

                if (!IsOptional(prop))
                    required.Add(prop.Name);
            }

            return new JsonSchemaBuilder()
                .Type("object")
                .Properties(props)
                .Required(required)
                .Build();
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
