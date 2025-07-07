using System;
using System.Text.Json.Nodes;

namespace Agenty.Core
{
    public interface ILLMClient
    {
        public void Initialize(string url, string apiKey, string modelName);
        public Task<string> GenerateResponse(string prompt);
        public IAsyncEnumerable<string> GenerateStreamingResponse(string prompt);
        Task<List<ToolCallInfo>> GetFunctionCallResponse(string prompt); // uses all registered
        Task<List<ToolCallInfo>> GetFunctionCallResponse(string prompt, List<Tool> tools); // custom
        public JsonObject GetStructuredResponse(string prompt, JsonObject responseFormat);
        public void SetSystemPrompt(string prompt);
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
        public string Name { get; set; }
        public JsonObject Parameters { get; set; }
    }
}
