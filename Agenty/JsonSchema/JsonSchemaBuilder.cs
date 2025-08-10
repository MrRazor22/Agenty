
using System.Text.Json.Nodes;

namespace Agenty.JsonSchema
{
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

        public JsonSchemaBuilder Properties(JsonObject properties)
        {
            _schema[JsonSchemaConstants.PropertiesKey] = properties;
            return this;
        }

        public JsonSchemaBuilder Required(JsonArray required)
        {
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

        public JsonObject Build() => _schema;

        // You can add helpers to read properties too, if needed
    }

}
