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
        public JsonObject SchemaDefinition { get; set; }
        [JsonIgnore] public Delegate? Function { get; set; }
        public override string ToString() => $"Tool: {Name} - {Description} | Args: {string.Join(", ", SchemaDefinition?["parameters"]?["properties"]?.AsObject().Select(p => p.Key) ?? [])}";
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

            return $"ToolCall: '{Name}' (id: {Id}) with Arguments: [{argsStr}]";
        }
    }
}
