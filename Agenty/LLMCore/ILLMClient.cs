using Agenty.AgentCore.Runtime;
using Agenty.LLMCore.ChatHandling;
using Agenty.LLMCore.Messages;
using Agenty.LLMCore.ToolHandling;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Agenty.LLMCore
{
    public interface ILLMClient
    {
        void Initialize(string url, string apiKey, string modelName);

        Task<LLMResponse> GetResponse(
            Conversation prompt,
            ReasoningMode mode = ReasoningMode.Balanced,
            string? model = null,
            LLMCallOptions? opts = null,
            CancellationToken ct = default);

        IAsyncEnumerable<string> GetStreamingResponse(
            Conversation prompt,
            ReasoningMode mode = ReasoningMode.Balanced,
            string? model = null,
            CancellationToken ct = default);

        Task<LLMResponse> GetToolCallResponse(
            Conversation prompt,
            IEnumerable<Tool> tools,
            ToolCallMode toolCallMode = ToolCallMode.Auto,
            ReasoningMode mode = ReasoningMode.Balanced,
            string? model = null,
            LLMCallOptions? opts = null,
            CancellationToken ct = default);

        Task<LLMResponse> GetStructuredResponse(
            Conversation prompt,
            JObject responseFormat,
            ReasoningMode mode = ReasoningMode.Balanced,
            string? model = null,
            LLMCallOptions? opts = null,
            CancellationToken ct = default);
    }

    public enum ToolCallMode
    {
        None,     // expose tools but forbid calls
        Auto,     // allow text or tool calls
        Required,  // force tool call
        OneTool   // force exactly one tool call
    }

    public enum ReasoningMode
    {
        Deterministic,  // gates, routing, grading
        Planning,       // structured step-by-step planning
        Balanced,       // normal reasoning
        Creative        // brainstorming / open ended
    }

    public sealed class LLMResponse
    {
        public string? AssistantMessage { get; set; }

        public JToken? StructuredResult { get; set; }

        public List<ToolCall> ToolCalls { get; set; } = new List<ToolCall>();
        public string? FinishReason { get; set; }
        public int? InputTokens { get; set; }
        public int? OutputTokens { get; set; }
        public int? TotalTokens => (InputTokens ?? 0) + (OutputTokens ?? 0);

        public LLMResponse() { }

        public LLMResponse(string? assistantMessage)
        {
            AssistantMessage = assistantMessage;
        }

        public LLMResponse(
            string? assistantMessage = null,
            JToken? structuredResult = null,
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
