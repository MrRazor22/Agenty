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
    public abstract class LLMRequestBase
    {
        public Conversation Prompt { get; internal set; }
        public string Model { get; }
        public ReasoningMode Reasoning { get; }
        public LLMSamplingOptions Sampling { get; }

        protected LLMRequestBase(
            Conversation prompt,
            string model = null,
            ReasoningMode reasoning = ReasoningMode.Balanced,
            LLMSamplingOptions sampling = null)
        {
            Prompt = prompt;
            Model = model;
            Reasoning = reasoning;
            Sampling = sampling;
        }

        public abstract LLMRequestBase DeepClone();
    }
    public sealed class LLMRequest : LLMRequestBase
    {
        public ToolCallMode ToolCallMode { get; }
        public IEnumerable<Tool> AllowedTools { get; internal set; }

        public LLMRequest(
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

        public override LLMRequestBase DeepClone()
        {
            return new LLMRequest(
                prompt: Prompt.Clone(),     // **only deep clone here**
                toolCallMode: ToolCallMode,
                allowedTools: AllowedTools, // allowed to share — immutable list
                model: Model,
                reasoning: Reasoning,
                sampling: Sampling
            );
        }
    }
    public sealed class LLMStructuredRequest : LLMRequestBase
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
            ResultType = resultType;
            Schema = Schema;
            AllowedTools = allowedTools;
            ToolCallMode = toolCallMode;
        }

        public override LLMRequestBase DeepClone()
        {
            return new LLMStructuredRequest(
                prompt: Prompt.Clone(),        // **deep clone prompt only**
                resultType: ResultType,
                allowedTools: AllowedTools,    // immutable list shared
                toolCallMode: ToolCallMode,
                model: Model,
                reasoning: Reasoning,
                sampling: Sampling
            )
            {
                Schema = Schema                 // Schema is immutable — share
            };
        }
    }
    public sealed class LLMInitOptions
    {
        public string BaseUrl { get; set; } = "";
        public string ApiKey { get; set; } = "";
        public string Model { get; set; } = "";
    }

    public interface ILLMClient
    {
        Task<LLMResponse> ExecuteAsync(
            LLMRequest request,
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

    public abstract class LLMResponseBase
    {
        public string FinishReason { get; }
        public int InputTokens { get; }
        public int OutputTokens { get; }

        protected LLMResponseBase(
            string finishReason,
            int inputTokens,
            int outputTokens)
        {
            FinishReason = finishReason ?? "stop";
            InputTokens = inputTokens;
            OutputTokens = outputTokens;
        }
    }

    public sealed class LLMResponse : LLMResponseBase
    {
        public string? AssistantMessage { get; }
        public List<ToolCall> ToolCalls { get; }

        public LLMResponse(
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

    public sealed class LLMStructuredResponse<T> : LLMResponseBase
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
