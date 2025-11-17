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
    public class LLMRequest
    {
        public Conversation Prompt { get; }
        public string Model { get; }
        public ReasoningMode Reasoning { get; }
        public LLMSamplingOptions Sampling { get; }

        protected LLMRequest(
            Conversation prompt,
            string model = null,
            ReasoningMode reasoning = ReasoningMode.Balanced,
            LLMSamplingOptions sampling = null)
        {
            Prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
            Model = model;
            Reasoning = reasoning;
            Sampling = sampling;
        }
    }
    public sealed class LLMToolRequest : LLMRequest
    {
        public ToolCallMode ToolCallMode { get; }
        public IEnumerable<Tool> AllowedTools { get; internal set; }

        public LLMToolRequest(
            Conversation prompt,
            ToolCallMode toolCallMode = ToolCallMode.Auto,
            IEnumerable<Tool> allowedTools = null,
            string model = null,
            ReasoningMode reasoning = ReasoningMode.Balanced,
            LLMSamplingOptions sampling = null)
            : base(prompt, model, reasoning, sampling)
        {
            AllowedTools = allowedTools;
            ToolCallMode = toolCallMode;
        }
    }

    public sealed class LLMStructuredRequest : LLMRequest
    {
        public Type ResultType { get; internal set; }
        public JObject Schema { get; internal set; }

        public IEnumerable<Tool> AllowedTools { get; internal set; }
        public ToolCallMode ToolCallMode { get; }

        public LLMStructuredRequest(
            Conversation prompt,
            Type resultType,
            IEnumerable<Tool> allowedTools = null,
            ToolCallMode toolCallMode = ToolCallMode.Disabled,
            string model = null,
            ReasoningMode reasoning = ReasoningMode.Balanced,
            LLMSamplingOptions sampling = null)
            : base(prompt, model, reasoning, sampling)
        {
            ResultType = resultType ?? throw new ArgumentNullException(nameof(resultType));
            AllowedTools = allowedTools;
            ToolCallMode = toolCallMode;
        }
    }

    public interface ILLMClient
    {
        Task<LLMTextAndToolCallResponse> ExecuteAsync(
            LLMToolRequest request,
            CancellationToken ct = default,
            Action<LLMStreamChunk>? onStream = null);

        Task<LLMStructuredResponse<T>> ExecuteAsync<T>(
            LLMStructuredRequest request,
            CancellationToken ct = default);

        Task<IReadOnlyList<ToolCallResult>> RunToolCalls(
            List<ToolCall> calls,
            CancellationToken ct = default);
    }


    public enum ToolCallMode
    {
        None,     // expose tools but forbid calls
        Auto,     // allow text or tool calls
        Required,  // force tool call
        OneTool,   // force exactly one tool call
        Disabled,  // Don't send tools to LLM at all
    }

    public enum ReasoningMode
    {
        Deterministic,  // gates, routing, grading
        Planning,       // structured step-by-step planning
        Balanced,       // normal reasoning
        Creative        // brainstorming / open ended
    }
    public sealed class LLMSamplingOptions
    {
        public float? Temperature { get; set; }
        public float? TopP { get; set; }
        public int? MaxOutputTokens { get; set; }
    }

    public abstract class LLMResponse
    {
        public string FinishReason { get; }
        public int InputTokens { get; }
        public int OutputTokens { get; }

        protected LLMResponse(
            string finishReason,
            int inputTokens,
            int outputTokens)
        {
            FinishReason = finishReason ?? "stop";
            InputTokens = inputTokens;
            OutputTokens = outputTokens;
        }
    }

    public sealed class LLMTextAndToolCallResponse : LLMResponse
    {
        public string? AssistantMessage { get; }
        public List<ToolCall> ToolCalls { get; }

        public LLMTextAndToolCallResponse(
            string? assistantMessage,
            List<ToolCall> toolCalls,
            string finishReason,
            int input,
            int output)
            : base(finishReason, input, output)
        {
            AssistantMessage = assistantMessage;
            ToolCalls = toolCalls ?? throw new ArgumentNullException(nameof(toolCalls));
        }
    }

    public sealed class LLMStructuredResponse<T> : LLMResponse
    {
        public JToken RawJson { get; }
        public T Result { get; }

        public LLMStructuredResponse(
            JToken rawJson,
            T result,
            string finishReason,
            int inputTokens,
            int outputTokens)
            : base(finishReason, inputTokens, outputTokens)
        {
            RawJson = rawJson;
            Result = result;
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
