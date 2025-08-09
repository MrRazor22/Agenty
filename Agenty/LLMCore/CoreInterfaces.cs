using System;
using System.Collections;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Agenty.LLMCore
{
    public enum Role { System, Assistant, User, Tool }
    public record Chat(Role Role, string? Content, Tool? toolCallInfo = null);
    public class ChatHistory() : List<Chat>
    {
        public ChatHistory Add(Role role, string? content = null, Tool? tool = null)
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
        Task<Tool> GetToolCallResponse(ChatHistory prompt, IToolManager tools);
        Task<Tool> GetToolCallResponse(ChatHistory prompt, params Tool[] tools);
        Task<JsonObject> GetStructuredResponse(ChatHistory prompt, JsonObject responseFormat);
    }

    public interface IToolManager
    {
        IReadOnlyList<Tool> RegisteredTools { get; }
        void Register(params Delegate[] funcs);
        void RegisterAll(Type type);
        Tool? Get(Delegate func);
        Tool? Get(string toolName);
        bool Contains(string toolName);
        JsonObject GetToolsSchema();
        object?[] ParseToolParams(string toolName, JsonObject arguments);
        Tool? TryExtractInlineToolCall(string content);
        Task<T?> Invoke<T>(Tool toolCall);
    }


    public class Tool
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public JsonObject ArgsRegisteredSchema { get; set; }
        public JsonObject ArgsToolCallSchema { get; set; }
        public object?[] Parameters { get; set; }

        [JsonIgnore]
        public string? AssistantMessage { get; set; } // non-null if no tool call

        [JsonIgnore]
        public bool forceToolCall { get; set; } = false;

        [JsonIgnore]
        public Delegate? Function { get; set; }
        public override string ToString() => $"Tool Info: '{Name}' (id: {Id}) with Parameters: " +
            $"[{string.Join(", ", Parameters?.Select(p => p?.ToString() ?? "null")
                ?? Enumerable.Empty<string>())}]";
    }

}
