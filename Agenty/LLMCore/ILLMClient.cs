using Agenty.LLMCore.ChatHandling;
using Agenty.LLMCore.Messages;
using Agenty.LLMCore.ToolHandling;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Agenty.LLMCore
{
    public interface ILLMClient
    {
        void Initialize(string url, string apiKey, string modelName);

        Task<LLMResponse> GetResponse(Conversation prompt, LLMMode mode = LLMMode.Balanced);
        IAsyncEnumerable<string> GetStreamingResponse(Conversation prompt, LLMMode mode = LLMMode.Balanced);
        Task<LLMResponse> GetToolCallResponse(
            Conversation prompt,
            IEnumerable<Tool> tools,
            ToolCallMode toolCallMode = ToolCallMode.Auto,
            LLMMode mode = LLMMode.Balanced);
        Task<LLMResponse> GetStructuredResponse(
            Conversation prompt,
            JObject responseFormat,
            LLMMode mode = LLMMode.Balanced);
    }

    public enum ToolCallMode
    {
        None,     // expose tools but forbid calls
        Auto,     // allow text or tool calls
        Required  // force tool call
    }

    public enum LLMMode
    {
        Deterministic,  // gates, routing, grading
        Planning,       // structured step-by-step planning
        Balanced,       // normal reasoning
        Creative        // brainstorming / open ended
    }

    public sealed class LLMResponse
    {
        public string? AssistantMessage { get; set; }
        public JObject? StructuredResult { get; set; }   // <-- Newtonsoft JObject
        public List<ToolCall> ToolCalls { get; set; } = new List<ToolCall>();
        public string? FinishReason { get; set; }

        public LLMResponse() { }

        public LLMResponse(string? assistantMessage)
        {
            AssistantMessage = assistantMessage;
        }

        public LLMResponse(
            string? assistantMessage = null,
            JObject? structuredResult = null,   // <-- JObject
            List<ToolCall>? toolCalls = null,
            string? finishReason = null)
        {
            AssistantMessage = assistantMessage;
            StructuredResult = structuredResult;
            ToolCalls = toolCalls ?? new List<ToolCall>();
            FinishReason = finishReason;
        }
    }
}
