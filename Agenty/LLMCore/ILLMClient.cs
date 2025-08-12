using System;
using System.Collections;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Agenty.LLMCore
{
    public interface ILLMClient
    {
        void Initialize(string url, string apiKey, string modelName);
        Task<string> GetResponse(Conversation prompt);
        IAsyncEnumerable<string> GetStreamingResponse(Conversation prompt);
        Task<ToolCall> GetToolCallResponse(Conversation prompt, IEnumerable<Tool> tools, bool forceToolCall = false);
        Task<JsonObject> GetStructuredResponse(Conversation prompt, JsonObject responseFormat);
    }

}
