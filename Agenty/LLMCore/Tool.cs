using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
            return !string.IsNullOrWhiteSpace(Description)
                ? $"{Name} → {Description}"
                : $"{Name}";
        }

    }

    public class ToolCall(string id, string name, JsonObject arguments, object?[]? parameters = null, string? message = null)
    {
        public string Id { get; private set; } = id;
        public string Name { get; private set; } = name;
        public JsonObject Arguments { get; private set; } = arguments;
        public string? AssistantMessage { get; set; } = message;
        [JsonIgnore] public object?[]? Parameters { get; private set; } = parameters;

        // Secondary constructor: message-only
        public ToolCall(string message)
            : this("", "", new JsonObject(), [], message) { }

        public override string ToString()
        {
            var argsStr = Arguments != null && Arguments.Count > 0
                ? string.Join(", ", Arguments.Select(kvp => $"{kvp.Key}: {kvp.Value?.ToJsonString()}"))
                : "none";

            return $"Name: '{Name}' (id: {Id}) with Arguments: [{argsStr}]";
        }
    }
}
