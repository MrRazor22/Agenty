using System;
using System.Text.Json.Nodes;

namespace Agenty.Core
{
    public interface ILLMClient
    {
        public void Initialize(string url, string apiKey, string modelName);
        public Task<string> GenerateResponse(IPrompt prompt);
        public IAsyncEnumerable<string> GenerateStreamingResponse(IPrompt prompt);
        Task<List<ToolCallInfo>> GetFunctionCallResponse(IPrompt prompt); // uses all registered
        Task<List<ToolCallInfo>> GetFunctionCallResponse(IPrompt prompt, List<Tool> tools); // custom
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
    }

    public enum ChatRole
    {
        Assistant,
        User,
        Tool
    }

    public record ChatInput(ChatRole Role, string Content, string? ToolId = null);

    public interface IPrompt
    {
        IEnumerable<ChatInput> Messages { get; }
    }
}
