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
    }

    public interface ILLMClient
    {
        void Initialize(string url, string apiKey, string modelName);
        Task<string> GetResponse(ChatHistory prompt);
        IAsyncEnumerable<string> GetStreamingResponse(ChatHistory prompt);
        Task<Tool> GetToolCallResponse(ChatHistory prompt, ITools tools);
        Task<Tool> GetToolCallResponse(ChatHistory prompt, params Tool[] tools);
        Task<JsonObject> GetStructuredResponse(ChatHistory prompt, JsonObject responseFormat);
    }

    public interface ITools : IEnumerable<Tool>
    {
        IReadOnlyList<Tool> RegisteredTools { get; }
        void Register(params Delegate[] funcs);
        void RegisterAll(Type type);
        Tool? Get(Delegate func);
        bool Contains(string toolName);
        JsonObject GetResponseFormatSchema();
        T? Invoke<T>(Tool toolCall);
    }


    public class Tool
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public JsonObject Parameters { get; set; }
        public string? AssistantMessage { get; set; } // non-null if no tool call
        public bool forceToolCall { get; set; } = false;

        [JsonIgnore]
        public Delegate? Function { get; set; }
        [JsonIgnore]
        private object? _toolResponse;
        public void SetToolResponse<T>(T response) => _toolResponse = response;
        public T GetToolResponse<T>() => (T)_toolResponse!;
        public override string ToString()
        {
            var args = Parameters?.Select(kv => $"{kv.Key}: {kv.Value}") ?? Enumerable.Empty<string>();
            var argString = string.Join(", ", args);
            return $"Tool Info: '{Name}' (id: {Id}) with {argString}";
        }
    }

}
