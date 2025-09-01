using Agenty.LLMCore.Providers.OpenAI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Agenty.LLMCore.ToolHandling
{
    public class Tool
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public JsonObject ParametersSchema { get; set; }
        [JsonIgnore] public Delegate? Function { get; set; }
        [JsonIgnore] public List<string> Tags { get; set; } = new();
        public override string ToString()
        {
            var props = ParametersSchema?["properties"]?.AsObject();
            var args = props != null
                ? string.Join(", ", props.Select(p => p.Key))
                : "";

            var argPart = args.Length > 0 ? $"({args})" : "()";

            return !string.IsNullOrWhiteSpace(Description)
                ? $"{Name}{argPart} => {Description}"
                : $"{Name}{argPart}";
            //return Name;
        }
    }

    public class ToolCall
    {
        [JsonConstructor] // 👈 tells serializer to use this
        public ToolCall(string id, string name, JsonObject arguments)
        {
            Id = id;
            Name = name;
            Arguments = arguments;
        }

        public ToolCall(string id, string name, JsonObject arguments, object?[]? parameters = null, string? message = null)
            : this(id, name, arguments)
        {
            Parameters = parameters;
            Message = message;
        }

        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; private set; }

        [JsonPropertyName("arguments")]
        public JsonObject Arguments { get; private set; }

        [JsonIgnore]
        public object?[]? Parameters { get; private set; }

        [JsonIgnore]
        public string? Message { get; set; }

        // message-only ctor stays fine
        public ToolCall(string message) : this("", "", new JsonObject()) => Message = message;

        public static ToolCall Empty { get; } = new("", "", new JsonObject());

        public bool IsEmpty =>
            string.IsNullOrWhiteSpace(Name) &&
            string.IsNullOrWhiteSpace(Message);

        public override string ToString()
        {
            var argsStr = Arguments is { Count: > 0 }
                ? string.Join(", ", Arguments.Select(kvp => $"{kvp.Key}: {kvp.Value?.ToJsonString()}"))
                : "none";

            return $"Name: '{Name}' (id: {Id}) with Arguments: [{argsStr}]";
        }
    }
}
