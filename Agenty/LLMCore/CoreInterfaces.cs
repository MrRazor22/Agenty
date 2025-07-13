using System;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Agenty.LLMCore
{
    public interface ILLMClient
    {
        void Initialize(string url, string apiKey, string modelName);
        Task<string> GetResponse(IPrompt prompt);
        IAsyncEnumerable<string> GetStreamingResponse(IPrompt prompt);
        Task<ToolCallResponse> GetFunctionCallResponse(IPrompt prompt, List<Tool> tools);
        Task<JsonObject> GetStructuredResponse(IPrompt prompt, JsonObject responseFormat);
    }

    public interface IToolRegistry
    {
        void Register(Delegate func, params string[] tags);
        void RegisterAll(List<Delegate> funcs);
        void RegisterAll(params Delegate[] funcs);
        List<Tool> GetRegisteredTools();
        List<Tool> GetToolsByTag(string tag);
    }

    public interface IToolExecutor
    {
        string? InvokeTool(ToolCallInfo toolCall);
    }

    public class ToolCallResponse
    {
        public string? AssistantMessage { get; set; } // non-null if no tool call
        public List<ToolCallInfo> ToolCalls { get; set; } = new();
    }


    public class Tool
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public JsonObject ParameterSchema { get; set; }
        public List<string> Tags { get; set; } = new();

        [JsonIgnore]
        public Delegate? Function { get; set; }

        public override string ToString()
        {
            return $"'{Name}' - {Description}";
        }
    }

    public class ToolCallInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public JsonObject Parameters { get; set; }

        public override string ToString()
        {
            var args = Parameters?.Select(kv => $"{kv.Key}: {kv.Value}") ?? Enumerable.Empty<string>();
            var argString = string.Join(", ", args);
            return $"Tool Call Info: '{Name}' (id: {Id}) with {argString}";
        }
    }

    public enum ChatRole
    {
        System,
        Assistant,
        User,
        Tool
    }

    public record ChatInput(ChatRole Role, string Content, ToolCallInfo? toolCallInfo = null);

    public interface IPrompt
    {
        IEnumerable<ChatInput> Messages { get; }
        void Add(ChatRole Role, string Content, ToolCallInfo? toolCallInfo = null);
        void AddMany(params (ChatRole role, string content, ToolCallInfo? toolCallInfo)[] messages);
        bool RemoveLast(ChatRole? role = null); // optional filter
        bool RemoveMessage(Predicate<ChatInput> match);
        void Clear();
    }
}
