using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace AgentCore.JsonSchema
{
    public static class JsonSchemaExtensions
    {
        public static JObject GetSchemaFor<T>() => typeof(T).GetSchemaForType();

        public static JObject GetSchemaForType(this Type type, HashSet<Type>? visited = null)
        {
            visited ??= new HashSet<Type>();
            type = Nullable.GetUnderlyingType(type) ?? type;

            if (type.IsEnum)
            {
                var typeDesc = type.GetCustomAttribute<DescriptionAttribute>()?.Description;

                return new JsonSchemaBuilder()
                    .Type<string>()
                    .Enum(Enum.GetNames(type))
                    .Description(typeDesc ?? $"One of: {string.Join(", ", Enum.GetNames(type))}")
                    .Build();
            }

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
                return GetDictionarySchema(type.GetGenericArguments()[1], visited);
            }

            if (visited.Contains(type))
            {
                // break recursion loops
                return new JsonSchemaBuilder()
                    .Type<object>()
                    .Build();
            }

            visited.Add(type);

            var props = new JObject();
            var required = new JArray();

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.GetCustomAttribute<System.Text.Json.Serialization.JsonIgnoreAttribute>() != null)
                    continue;

                var propType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                var propSchema = propType.GetSchemaForType(visited);

                // description
                if (prop.GetCustomAttribute<DescriptionAttribute>() is { } descAttr &&
                    !string.IsNullOrEmpty(descAttr.Description))
                {
                    propSchema[JsonSchemaConstants.DescriptionKey] = descAttr.Description;
                }

                // email
                if (prop.GetCustomAttribute<EmailAddressAttribute>() != null)
                    propSchema[JsonSchemaConstants.FormatKey] = "email";

                // length
                if (prop.GetCustomAttribute<StringLengthAttribute>() is { } len)
                {
                    propSchema[JsonSchemaConstants.MinLengthKey] = len.MinimumLength;
                    propSchema[JsonSchemaConstants.MaxLengthKey] = len.MaximumLength;
                }

                // regex
                if (prop.GetCustomAttribute<RegularExpressionAttribute>() is { } regex)
                    propSchema[JsonSchemaConstants.PatternKey] = regex.Pattern;

                // range
                if (prop.GetCustomAttribute<RangeAttribute>() is { } range)
                {
                    if (range.Minimum != null && double.TryParse(range.Minimum.ToString(), out var min))
                        propSchema[JsonSchemaConstants.MinimumKey] = min;
                    if (range.Maximum != null && double.TryParse(range.Maximum.ToString(), out var max))
                        propSchema[JsonSchemaConstants.MaximumKey] = max;
                }

                // default
                if (prop.GetCustomAttribute<DefaultValueAttribute>() is { } dv)
                    propSchema[JsonSchemaConstants.DefaultKey] = JToken.FromObject(dv.Value);

                props[prop.Name] = propSchema;

                if (!prop.IsOptional())
                    required.Add(prop.Name);
            }

            return new JsonSchemaBuilder()
                .Type<object>()
                .Properties(props)
                .Required(required)
                .AdditionalProperties(false)
                .Build();
        }

        private static JObject GetDictionarySchema(Type valueType, HashSet<Type> visited)
        {
            var valueSchema = valueType.GetSchemaForType(visited);
            return new JsonSchemaBuilder()
                .Type("object")
                .AdditionalProperties(valueSchema)
                .Build();
        }

        private static bool IsOptional(this PropertyInfo prop)
        {
            if (Nullable.GetUnderlyingType(prop.PropertyType) != null) return true;
            if (prop.GetCustomAttribute<DefaultValueAttribute>() != null) return true;
            if (IsNullableReference(prop)) return true;
            return false;
        }

        private static bool IsNullableReference(PropertyInfo prop)
        {
            var nullAttr = prop.GetCustomAttributes().FirstOrDefault(a => a.GetType().Name == "NullableAttribute");
            if (nullAttr != null)
            {
                var flags = nullAttr.GetType().GetField("NullableFlags", BindingFlags.Public | BindingFlags.Instance);
                var val = flags?.GetValue(nullAttr);
                if (val is byte b) return b == 2;
                if (val is byte[] arr && arr.Length > 0) return arr[0] == 2;
            }

            var ctxAttr = prop.DeclaringType?.GetCustomAttributes().FirstOrDefault(a => a.GetType().Name == "NullableContextAttribute");
            if (ctxAttr != null)
            {
                var flagField = ctxAttr.GetType().GetField("Flag", BindingFlags.Public | BindingFlags.Instance);
                var f = flagField?.GetValue(ctxAttr);
                if (f is byte fb) return fb == 2;
            }

            var asmCtx = prop.Module.Assembly.GetCustomAttributes().FirstOrDefault(a => a.GetType().Name == "NullableContextAttribute");
            if (asmCtx != null)
            {
                var flagField = asmCtx.GetType().GetField("Flag", BindingFlags.Public | BindingFlags.Instance);
                var f = flagField?.GetValue(asmCtx);
                if (f is byte fb) return fb == 2;
            }

            return false;
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
            if (Nullable.GetUnderlyingType(type) is Type underlyingType) type = underlyingType;
            if (type.IsEnum) return "string";
            if (type == typeof(string) || type == typeof(char)) return "string";
            if (type == typeof(bool)) return "boolean";
            if (type == typeof(float) || type == typeof(double) || type == typeof(decimal)) return "number";
            if (type == typeof(void) || type == typeof(DBNull)) return "null";
            if (type.IsArray || typeof(IEnumerable).IsAssignableFrom(type) && type != typeof(string)) return "array";
            if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte) ||
                type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort) || type == typeof(sbyte))
                return "integer";
            return "object";
        }
    }
}
