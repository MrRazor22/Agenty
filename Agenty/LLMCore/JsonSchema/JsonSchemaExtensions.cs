using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Agenty.LLMCore.JsonSchema
{
    public static class JsonSchemaExtensions
    {
        public static string AsJSONString(this object? obj)
        {
            if (obj == null) return "<null>";
            return obj is string s ? s : JsonSerializer.Serialize(obj);
        }
        public static JsonObject GetSchemaFor<T>()
        {
            return GetSchemaForType(typeof(T));
        }

        public static JsonObject GetSchemaForType(this Type type, HashSet<Type>? visited = null)
        {
            visited ??= new HashSet<Type>();
            type = Nullable.GetUnderlyingType(type) ?? type;

            if (type.IsEnum)
                return new JsonSchemaBuilder()
                    .Type<string>()
                    .Enum(Enum.GetNames(type))
                    .Description($"One of: {string.Join(", ", Enum.GetNames(type))}")
                    .Build();

            if (type.IsSimpleType())
                return new JsonSchemaBuilder()
                    .Type(type.MapClrTypeToJsonType())
                    .Build();

            if (type.IsArray)
                return new JsonSchemaBuilder()
                    .Type<Array>()
                    .Items(type.GetElementType()!.GetSchemaForType(visited))
                    .Build();

            if (typeof(IEnumerable).IsAssignableFrom(type) && type.IsGenericType)
                return new JsonSchemaBuilder()
                    .Type<Array>()
                    .Items(type.GetGenericArguments()[0].GetSchemaForType(visited))
                    .Build();

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>) &&
    type.GetGenericArguments()[0] == typeof(string))
            {
                var valueType = type.GetGenericArguments()[1];
                var valueSchema = new JsonSchemaBuilder()
                    .Type("string") // 🔑 force string
                    .Build();

                return new JsonSchemaBuilder()
                    .Type("object")
                    .AdditionalProperties(valueSchema)
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

                if (!string.IsNullOrEmpty(prop.GetCustomAttribute<DescriptionAttribute>()?.Description))
                {
                    var description = prop.GetCustomAttribute<DescriptionAttribute>()?.Description;
                    propSchema[JsonSchemaConstants.DescriptionKey] = description;
                }

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

                if (!prop.IsOptional())
                    required.Add(prop.Name);
            }

            return new JsonSchemaBuilder()
                .Type<object>()
                .Properties(props)
                .Required(required)
                //.AdditionalProperties(false)
                .Build();
        }

        private static bool IsOptional(this PropertyInfo prop)
        {
            var type = prop.PropertyType;
            return Nullable.GetUnderlyingType(type) != null || type.IsClass && type != typeof(string);
        }
        public static bool IsSimpleType(this Type type) =>
            type.IsPrimitive ||
            type == typeof(string) ||
            type == typeof(decimal) ||
            type == typeof(DateTime) ||
            type == typeof(Guid);

        public static string MapClrTypeToJsonType(this Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            // Handle nullable types: Nullable<T> => T
            if (Nullable.GetUnderlyingType(type) is Type underlyingType) type = underlyingType;
            if (type.IsEnum) return "string"; // JSON Schema enums are strings with enum keyword, so treat enum as string here 
            if (type == typeof(string) || type == typeof(char)) return "string";
            if (type == typeof(bool)) return "boolean";
            if (type == typeof(float) || type == typeof(double) || type == typeof(decimal)) return "number";
            if (type == typeof(void) || type == typeof(DBNull)) return "null";
            if (type.IsArray || typeof(System.Collections.IEnumerable).IsAssignableFrom(type) && type != typeof(string)) return "array";
            if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte) ||
                type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort) || type == typeof(sbyte))
                return "integer";
            // For all other types, treat as "object"
            return "object";
        }

    }
}
