using System;
using System.Collections;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;

namespace Agenty.LLMCore.JsonSchema
{
    public static class JsonSchemaExtensions
    {
        public static JsonObject GetSchemaFor<T>() => GetSchemaForType(typeof(T));

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

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>) && type.GetGenericArguments()[0] == typeof(string))
            {
                return GetDictionarySchema(type.GetGenericArguments()[1], visited);
            }

            if (visited.Contains(type))
            {
                // return a safe object placeholder for recursive types (avoids infinite loop)
                return new JsonSchemaBuilder()
                    .Type<object>()
                    .Build();
            }

            visited.Add(type);

            var props = new JsonObject();
            var required = new JsonArray();

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.GetCustomAttribute<System.Text.Json.Serialization.JsonIgnoreAttribute>() != null)
                    continue;

                var propType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                var propSchema = propType.GetSchemaForType(visited);

                // map description/email/stringlength/regex/range
                if (!string.IsNullOrEmpty(prop.GetCustomAttribute<DescriptionAttribute>()?.Description))
                    propSchema[JsonSchemaConstants.DescriptionKey] = prop.GetCustomAttribute<DescriptionAttribute>()!.Description;

                if (prop.GetCustomAttribute<EmailAddressAttribute>() != null)
                    propSchema[JsonSchemaConstants.FormatKey] = "email";

                if (prop.GetCustomAttribute<StringLengthAttribute>() is { } len)
                {
                    propSchema[JsonSchemaConstants.MinLengthKey] = len.MinimumLength;
                    propSchema[JsonSchemaConstants.MaxLengthKey] = len.MaximumLength;
                }

                if (prop.GetCustomAttribute<RegularExpressionAttribute>() is { } regex)
                    propSchema[JsonSchemaConstants.PatternKey] = regex.Pattern;

                if (prop.GetCustomAttribute<RangeAttribute>() is { } range)
                {
                    if (double.TryParse(range.Minimum?.ToString() ?? "", out var min))
                        propSchema[JsonSchemaConstants.MinimumKey] = min;
                    if (double.TryParse(range.Maximum?.ToString() ?? "", out var max))
                        propSchema[JsonSchemaConstants.MaximumKey] = max;
                }

                // default value
                if (prop.GetCustomAttribute<DefaultValueAttribute>() is { } dv)
                    propSchema[JsonSchemaConstants.DefaultKey] = JsonValue.Create(dv.Value);

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

        private static JsonObject GetDictionarySchema(Type valueType, HashSet<Type> visited)
        {
            var valueSchema = valueType.GetSchemaForType(visited);
            return new JsonSchemaBuilder()
                .Type("object")
                .AdditionalProperties(valueSchema)
                .Build();
        }

        private static bool IsOptional(this PropertyInfo prop)
        {
            // 1) nullable value types are optional
            if (Nullable.GetUnderlyingType(prop.PropertyType) != null) return true;

            // 2) DefaultValueAttribute marks optional
            if (prop.GetCustomAttribute<DefaultValueAttribute>() != null) return true;

            // 3) nullable reference type (C# 8+): detect compiler NullableAttribute / NullableContextAttribute
            if (IsNullableReference(prop)) return true;

            // otherwise required
            return false;
        }

        private static bool IsNullableReference(PropertyInfo prop)
        {
            // check on the property first
            var nullAttr = prop.GetCustomAttributes().FirstOrDefault(a => a.GetType().Name == "NullableAttribute");
            if (nullAttr != null)
            {
                // NullableAttribute usually has a byte[] or byte value; if present and indicates '2' or '1,2' => nullable.
                var flags = nullAttr.GetType().GetField("NullableFlags", BindingFlags.Public | BindingFlags.Instance);
                try
                {
                    var val = flags?.GetValue(nullAttr);
                    if (val is byte b) return b == 2;
                    if (val is byte[] arr && arr.Length > 0) return arr[0] == 2;
                }
                catch { /* fall through to context check */ }
            }

            // fallback: check NullableContextAttribute on declaring type or assembly
            var ctxAttr = prop.DeclaringType?.GetCustomAttributes().FirstOrDefault(a => a.GetType().Name == "NullableContextAttribute");
            if (ctxAttr != null)
            {
                var flagField = ctxAttr.GetType().GetField("Flag", BindingFlags.Public | BindingFlags.Instance);
                try
                {
                    var f = flagField?.GetValue(ctxAttr);
                    if (f is byte fb) return fb == 2;
                }
                catch { }
            }

            // last-resort: check assembly-level attribute
            var asmCtx = prop.Module.Assembly.GetCustomAttributes().FirstOrDefault(a => a.GetType().Name == "NullableContextAttribute");
            if (asmCtx != null)
            {
                var flagField = asmCtx.GetType().GetField("Flag", BindingFlags.Public | BindingFlags.Instance);
                try
                {
                    var f = flagField?.GetValue(asmCtx);
                    if (f is byte fb) return fb == 2;
                }
                catch { }
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
            if (type.IsArray || (typeof(IEnumerable).IsAssignableFrom(type) && type != typeof(string))) return "array";
            if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte) ||
                type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort) || type == typeof(sbyte))
                return "integer";
            return "object";
        }
    }
}
