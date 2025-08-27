using Agenty.LLMCore.Providers.OpenAI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Agenty.LLMCore
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
            //var props = ParametersSchema?["properties"]?.AsObject();
            //var args = props != null
            //    ? string.Join(", ", props.Select(p => p.Key))
            //    : "";

            //var argPart = args.Length > 0 ? $"({args})" : "()";

            //return !string.IsNullOrWhiteSpace(Description)
            //    ? $"{Name}{argPart} => {Description}"
            //    : $"{Name}{argPart}";
            return Name;
            //return this.ToOpenAiSchemaJson();
        }
    }

    public class ToolCall(string id, string name, JsonObject arguments, object?[]? parameters = null, string? message = null)
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = id;

        [JsonPropertyName("name")]
        public string Name { get; private set; } = name;

        [JsonPropertyName("arguments")]
        public JsonObject Arguments { get; private set; } = arguments;

        [JsonIgnore]
        public object?[]? Parameters { get; private set; } = parameters;

        [JsonIgnore] // Exclude AssistantMessage if not needed
        public string? AssistantMessage { get; set; } = message;

        // Secondary constructor: message-only
        public ToolCall(string message)
            : this("", "", new JsonObject(), [], message) { }

        // === Empty ToolCall singleton ===
        public static ToolCall Empty { get; } = new(
            id: string.Empty,
            name: string.Empty,
            arguments: new JsonObject(),
            parameters: null,
            message: string.Empty
        );

        // === Check if this is Empty ===
        public bool IsEmpty =>
            string.IsNullOrWhiteSpace(Name) &&
            string.IsNullOrWhiteSpace(AssistantMessage);

        public override string ToString()
        {
            var argsStr = Arguments != null && Arguments.Count > 0
                ? string.Join(", ", Arguments.Select(kvp => $"{kvp.Key}: {kvp.Value?.ToJsonString()}"))
                : "none";

            return $"Name: '{Name}' (id: {Id}) with Arguments: [{argsStr}]";
        }
    }
}
