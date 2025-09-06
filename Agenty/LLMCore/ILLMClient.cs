using Agenty.LLMCore.ToolHandling;
using System;
using System.Collections;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Agenty.LLMCore
{
    public interface IEmbeddingClient
    {
        Task<float[]> GetEmbeddingAsync(string input, string? model = null);
        Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(IEnumerable<string> inputs, string? model = null);
    }

    public interface ILLMClient
    {
        void Initialize(string url, string apiKey, string modelName);
        Task<string> GetResponse(Conversation prompt, LLMMode mode = LLMMode.Balanced);
        IAsyncEnumerable<string> GetStreamingResponse(Conversation prompt, LLMMode mode = LLMMode.Balanced);
        Task<LLMResponse> GetToolCallResponse(
            Conversation prompt,
            IEnumerable<Tool> tools,
            ToolCallMode toolCallMode = ToolCallMode.Auto,
            LLMMode mode = LLMMode.Deterministic);
        Task<JsonNode> GetStructuredResponse(Conversation prompt, JsonObject responseFormat, LLMMode mode = LLMMode.Deterministic);
    }
    public enum ToolCallMode
    {
        None,     // expose tools but forbid calls (Thought stage)
        Auto,     // allow text or tool calls (Action stage)
        Required  // force tool call (rare, like guardrails)
    }
    public enum LLMMode
    {
        Deterministic,  // gates, routing, grading
        Balanced,       // normal reasoning
        Creative        // brainstorming / open ended
    }
    public class LLMResponse
    {
        public string? AssistantMessage { get; set; }      // plain text reply, if any
        public List<ToolCall> ToolCalls { get; set; } = new(); // zero or more tool calls
        public string? FinishReason { get; set; }          // why the model stopped (stop, tool_call, length...)
    }

}
