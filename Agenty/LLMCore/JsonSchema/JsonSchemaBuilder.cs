using System.Text.Json.Nodes;

namespace Agenty.LLMCore.JsonSchema
{
    public static class JsonSchemaConstants
    { // Key constants
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
    }
    public class JsonSchemaBuilder
    {
        private readonly JsonObject _schema;

        public JsonSchemaBuilder()
        {
            _schema = new JsonObject();
        }

        public JsonSchemaBuilder(JsonObject existingSchema)
        {
            _schema = existingSchema ?? new JsonObject();
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

        public JsonSchemaBuilder Properties(JsonObject properties)
        {
            //if (properties != null && properties.Count > 0)
            _schema[JsonSchemaConstants.PropertiesKey] = properties;
            return this;
        }

        public JsonSchemaBuilder Required(JsonArray required)
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
            _schema[JsonSchemaConstants.EnumKey] = new JsonArray(values.Select(v => JsonValue.Create(v)).ToArray());
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

        public JsonSchemaBuilder AdditionalProperties(JsonObject additionalProps)
        {
            _schema[JsonSchemaConstants.AdditionalPropertiesKey] = additionalProps;
            return this;
        }

        public JsonSchemaBuilder Items(JsonNode items)
        {
            _schema[JsonSchemaConstants.ItemsKey] = items;
            return this;
        }

        /// <summary>
        /// Sets "anyOf" with the provided schemas array.
        /// </summary>
        public JsonSchemaBuilder AnyOf(params JsonNode[] schemas)
        {
            _schema["anyOf"] = new JsonArray(schemas);
            return this;
        }

        public JsonObject Build() => _schema;

        // You can add helpers to read properties too, if needed
    }

}
