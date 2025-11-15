using Agenty.AgentCore.Runtime;
using Agenty.LLMCore.ChatHandling;
using Agenty.LLMCore.Messages;
using Agenty.LLMCore.ToolHandling;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Agenty.LLMCore
{
    public interface ILLMClient
    {
        Task<T> GetStructuredTyped<T>(
            Conversation prompt,
            ToolCallMode toolCallMode = ToolCallMode.None,
            ReasoningMode mode = ReasoningMode.Deterministic,
            string model = null,
            LLMCallOptions opts = null,
            CancellationToken ct = default,
            params Tool[] tools);
        Task<LLMStructuredResult> GetStructured(
            Conversation prompt,
            Type targetType,
            ToolCallMode toolCallMode = ToolCallMode.None,
            ReasoningMode mode = ReasoningMode.Deterministic,
            string? model = null,
            LLMCallOptions? opts = null,
            CancellationToken ct = default,
            params Tool[] tools);

        Task<LLMTextToolCallResult> GetResponseStreaming(
             Conversation prompt,
             ToolCallMode toolCallMode = ToolCallMode.Auto,
             ReasoningMode mode = ReasoningMode.Balanced,
             string? model = null,
             LLMCallOptions? opts = null,
             CancellationToken ct = default,
             Action<LLMStreamChunk>? onChunk = null,
             params Tool[] tools);

        Task<IReadOnlyList<ToolCallResult>> RunToolCalls(
            List<ToolCall> toolCalls,
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
    public sealed class LLMCallOptions
    {
        public float? Temperature { get; set; }
        public float? TopP { get; set; }
        public int? MaxOutputTokens { get; set; }
    }

    public class LLMResult
    {
        public string AssistantMessage { get; protected set; }   // ALWAYS present
        public virtual object Payload { get; protected set; }    // optional extra
        public string FinishReason { get; protected set; }
        public int InputTokens { get; protected set; }
        public int OutputTokens { get; protected set; }

        public LLMResult(
            string assistantMessage,
            object payload,
            string finishReason,
            int inputTokens,
            int outputTokens)
        {
            AssistantMessage = assistantMessage ?? "";
            Payload = payload;
            FinishReason = finishReason ?? "stop";
            InputTokens = inputTokens;
            OutputTokens = outputTokens;
        }
    }

    public sealed class LLMTextToolCallResult : LLMResult
    {
        public new List<ToolCall> Payload { get; }

        public LLMTextToolCallResult(
            string assistantMessage,
            List<ToolCall> toolCalls,
            string finishReason,
            int input,
            int output)
            : base(assistantMessage, toolCalls, finishReason, input, output)
        {
            Payload = toolCalls;
        }
    }

    public sealed class LLMStructuredResult : LLMResult
    {
        public new JToken Payload { get; }

        public LLMStructuredResult(
            JToken payload,
            string finishReason,
            int input,
            int output)
            : base(null, payload, finishReason, input, output)
        {
            Payload = payload;
        }
    }

    public enum StreamKind
    {
        Text,
        ToolCall,
        Usage,
        Finish,
        // future:
        // Image,
        // Audio,
        // Json,
        // Reasoning
    }

    public readonly struct LLMStreamChunk
    {
        public StreamKind Kind { get; }
        public object? Payload { get; }     // unified extensible payload
        public string? FinishReason { get; }
        public int? InputTokens { get; }
        public int? OutputTokens { get; }

        public LLMStreamChunk(
            StreamKind kind,
            object? payload = null,
            string? finish = null,
            int? input = null,
            int? output = null)
        {
            Kind = kind;
            Payload = payload;
            FinishReason = finish;
            InputTokens = input;
            OutputTokens = output;
        }
    }
    public static class LLMStreamChunkExtensions
    {
        public static string? AsText(this LLMStreamChunk chunk)
            => chunk.Payload as string;

        public static ToolCall? AsToolCall(this LLMStreamChunk chunk)
            => chunk.Payload as ToolCall;
    }

}
