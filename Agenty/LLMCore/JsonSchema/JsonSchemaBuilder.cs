using Newtonsoft.Json.Linq;
using System.Linq;

namespace Agenty.LLMCore.JsonSchema
{
    public static class JsonSchemaConstants
    {
        public const string TypeKey = "type";
        public const string PropertiesKey = "properties";
        public const string RequiredKey = "required";
        public const string DescriptionKey = "description";
        public const string EnumKey = "enum";
        public const string FormatKey = "format";
        public const string MinLengthKey = "minLength";
        public const string MaxLengthKey = "maxLength";
        public const string PatternKey = "pattern";
        public const string AdditionalPropertiesKey = "additionalProperties";
        public const string ItemsKey = "items";
        public const string MinimumKey = "minimum";
        public const string MaximumKey = "maximum";
        public const string DefaultKey = "default";
    }

    public class JsonSchemaBuilder
    {
        private readonly JObject _schema;

        public JsonSchemaBuilder()
        {
            _schema = new JObject();
        }

        public JsonSchemaBuilder(JObject existingSchema)
        {
            _schema = existingSchema ?? new JObject();
        }

        public JsonSchemaBuilder Type(string type)
        {
            _schema[JsonSchemaConstants.TypeKey] = type;
            return this;
        }

        public JsonSchemaBuilder Type<T>()
        {
            var clrType = typeof(T);
            _schema[JsonSchemaConstants.TypeKey] = clrType.MapClrTypeToJsonType();
            return this;
        }

        public JsonSchemaBuilder Properties(JObject properties)
        {
            _schema[JsonSchemaConstants.PropertiesKey] = properties;
            return this;
        }

        public JsonSchemaBuilder Required(JArray required)
        {
            if (required != null && required.Count > 0)
                _schema[JsonSchemaConstants.RequiredKey] = required;
            return this;
        }

        public JsonSchemaBuilder Description(string description)
        {
            if (!string.IsNullOrWhiteSpace(description))
                _schema[JsonSchemaConstants.DescriptionKey] = description;
            return this;
        }

        public JsonSchemaBuilder Enum(string[] values)
        {
            _schema[JsonSchemaConstants.EnumKey] = new JArray(values.Select(v => new JValue(v)).ToArray());
            return this;
        }

        public JsonSchemaBuilder Format(string format)
        {
            if (!string.IsNullOrWhiteSpace(format))
                _schema[JsonSchemaConstants.FormatKey] = format;
            return this;
        }

        public JsonSchemaBuilder MinLength(int min)
        {
            _schema[JsonSchemaConstants.MinLengthKey] = min;
            return this;
        }

        public JsonSchemaBuilder MaxLength(int max)
        {
            _schema[JsonSchemaConstants.MaxLengthKey] = max;
            return this;
        }

        public JsonSchemaBuilder Pattern(string pattern)
        {
            if (!string.IsNullOrWhiteSpace(pattern))
                _schema[JsonSchemaConstants.PatternKey] = pattern;
            return this;
        }

        public JsonSchemaBuilder AdditionalProperties(bool allow)
        {
            _schema[JsonSchemaConstants.AdditionalPropertiesKey] = allow;
            return this;
        }

        public JsonSchemaBuilder AdditionalProperties(JObject additionalProps)
        {
            _schema[JsonSchemaConstants.AdditionalPropertiesKey] = additionalProps;
            return this;
        }

        public JsonSchemaBuilder Items(JToken items)
        {
            _schema[JsonSchemaConstants.ItemsKey] = items;
            return this;
        }

        public JsonSchemaBuilder AnyOf(params JToken[] schemas)
        {
            _schema["anyOf"] = new JArray(schemas);
            return this;
        }

        public JObject Build() => _schema;
    }
}
