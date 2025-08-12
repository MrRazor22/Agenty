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
        Task<string> GetResponse(Conversations prompt);
        IAsyncEnumerable<string> GetStreamingResponse(Conversations prompt);
        Task<ToolCall> GetToolCallResponse(Conversations prompt, IEnumerable<Tool> tools, bool forceToolCall = false);
        Task<JsonObject> GetStructuredResponse(Conversations prompt, JsonObject responseFormat);
    }

}
