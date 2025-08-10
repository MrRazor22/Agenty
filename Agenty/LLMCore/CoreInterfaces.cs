using System;
using System.Collections;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Agenty.LLMCore
{
    public enum Role { System, Assistant, User, Tool }
    public record Chat(Role Role, string? Content, ToolCall? toolCallInfo = null);
    public class ChatHistory() : List<Chat>
    {
        public ChatHistory Add(Role role, string? content = null, ToolCall? tool = null)
        {
            Add(new Chat(role, content, tool));
            return this;
        }
        public static ChatHistory Clone(ChatHistory original)
        {
            var copy = new ChatHistory();
            foreach (var message in original)
            {
                copy.Add(message.Role, message.Content, message.toolCallInfo);
            }
            return copy;
        }

    }

    public interface ILLMClient
    {
        void Initialize(string url, string apiKey, string modelName);
        Task<string> GetResponse(ChatHistory prompt);
        IAsyncEnumerable<string> GetStreamingResponse(ChatHistory prompt);
        Task<ToolCall> GetToolCallResponse(ChatHistory prompt, IEnumerable<Tool> tools, bool forceToolCall = false);
        Task<JsonObject> GetStructuredResponse(ChatHistory prompt, JsonObject responseFormat);
    }

    public interface ITools
    {
        IReadOnlyList<Tool> RegisteredTools { get; }
        void Register(params Delegate[] funcs);
        void RegisterAll(Type type);
        Tool? Get(Delegate func);
        Tool? Get(string toolName);
        bool Contains(string toolName);
    }

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
