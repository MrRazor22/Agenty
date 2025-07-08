using System;
using System.Text.Json.Nodes;

namespace Agenty.Core
{
    public interface ILLMClient
    {
        public void Initialize(string url, string apiKey, string modelName);
        public Task<string> GenerateResponse(IPrompt prompt);
        public IAsyncEnumerable<string> GenerateStreamingResponse(IPrompt prompt);
        Task<List<ToolCallInfo>> GetFunctionCallResponse(IPrompt prompt, List<Tool> tools);
        public JsonObject GetStructuredResponse(IPrompt prompt, JsonObject responseFormat);
    }

    public interface IToolRegistry
    {
        void Register(Delegate func, params string[] tags);
        void RegisterAll(List<Delegate> funcs);
        List<Tool> GetRegisteredTools();
        List<Tool> GetToolsByTag(string tag);
        string InvokeTool(ToolCallInfo toolCall);
    }

    public class Tool
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public JsonObject ParameterSchema { get; set; }
        public List<string> Tags { get; set; } = new();
    }

    public class ToolCallInfo
    {
        public string Id { get; set; }
        public string? AssistantMessage { get; set; }
        public string Name { get; set; }
        public JsonObject Parameters { get; set; }

        public override string ToString()
        {
            var args = Parameters?.Select(kv => $"{kv.Key}: {kv.Value}") ?? Enumerable.Empty<string>();
            var argString = string.Join(", ", args);
            return $"Initiated tool '{Name}' (id: {Id}) with {argString}";
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
    }
}
