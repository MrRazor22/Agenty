using Agenty.AgentCore.TokenHandling;
using Agenty.LLMCore;
using Agenty.LLMCore.ChatHandling;
using Agenty.LLMCore.JsonSchema;
using Agenty.LLMCore.Messages;
using Agenty.LLMCore.ToolHandling;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Agenty.AgentCore.Runtime
{
    // Slimmed return type for tool calls
    public class ToolCallResponse
    {
        public IReadOnlyList<ToolCall> Calls { get; }
        public string? AssistantMessage { get; }
        public string? FinishReason { get; }

        public ToolCallResponse(
            IReadOnlyList<ToolCall> calls,
            string? assistantMessage,
            string? finishReason
        )
        {
            Calls = calls;
            AssistantMessage = assistantMessage;
            FinishReason = finishReason;
        }
    }

    public interface ILLMCoordinator
    {
        Task<string?> GetResponse(
            Conversation prompt,
            ReasoningMode mode = ReasoningMode.Balanced,
            string? model = null);

        Task<T?> GetStructured<T>(
            Conversation prompt,
            ReasoningMode mode = ReasoningMode.Balanced,
            string? model = null)
            where T : class;

        Task<ToolCallResponse> GetToolCallResponse(
            Conversation prompt,
            ToolCallMode toolCallMode = ToolCallMode.Auto,
            ReasoningMode mode = ReasoningMode.Balanced,
            string? model = null,
            params Tool[] tools);

        Task<IReadOnlyList<ToolCallResult>> RunToolCalls(List<ToolCall> toolCalls);
    }



    internal sealed class LLMCoordinator : ILLMCoordinator
    {
        private readonly ILLMClient _llm;
        private readonly IToolCatalog _tools;
        private readonly IToolRuntime _Runtime;
        private readonly IToolCallParser _parser;
        private readonly ITokenManager _tokenManager;
        private readonly IRetryPolicy _retryPolicy;

        public LLMCoordinator(
            ILLMClient llm,
            IToolCatalog registry,
            IToolRuntime Runtime,
            IToolCallParser parser,
            ITokenManager tokenManager,
            IRetryPolicy retryPolicy)
        {
            _llm = llm;
            _tools = registry;
            _Runtime = Runtime;
            _parser = parser;
            _tokenManager = tokenManager;
            _retryPolicy = retryPolicy;
        }

        public async Task<string?> GetResponse(Conversation prompt, ReasoningMode mode = ReasoningMode.Balanced, string? model = null)
        {
            var resp = await _llm.GetResponse(prompt, mode, model);
            _tokenManager.Record(resp.InputTokens ?? 0, resp.OutputTokens ?? 0);
            return resp.AssistantMessage;
        }

        public async Task<T?> GetStructured<T>(Conversation prompt, ReasoningMode mode = ReasoningMode.Deterministic, string? model = null) where T : class
        {
            return await _retryPolicy.ExecuteAsync(intPrompt => RunStructuredOnce<T>(intPrompt, mode, model), prompt);
        }

        private async Task<T?> RunStructuredOnce<T>(Conversation intPrompt, ReasoningMode mode, string? model = null) where T : class
        {
            // build schema with Newtonsoft
            var schema = JsonSchemaExtensions.GetSchemaFor<T>(); // JObject
            var response = await _llm.GetStructuredResponse(intPrompt, schema, mode, model);
            _tokenManager.Record(response.InputTokens ?? 0, response.OutputTokens ?? 0);
            if (response?.StructuredResult == null)
            {
                intPrompt.AddAssistant($"Empty/invalid. Return valid JSON for {typeof(T).Name}.");
                return default;
            }

            var jsonNode = response.StructuredResult; // should be JToken/JObject
            var errors = _parser.ValidateAgainstSchema(jsonNode, schema, typeof(T).Name);

            if (errors.Any())
            {
                var msg = string.Join("; ", errors.Select(e => $"{e.Path}: {e.Message}"));
                intPrompt.AddAssistant($"Validation failed: {msg}. Return valid JSON for {typeof(T).Name}.");
                return default;
            }

            return jsonNode.ToObject<T>(JsonSerializer.Create(new JsonSerializerSettings
            {
                Converters = { new Newtonsoft.Json.Converters.StringEnumConverter() }
            }));
        }

        public async Task<ToolCallResponse> GetToolCallResponse(
    Conversation prompt,
    ToolCallMode toolCallMode = ToolCallMode.Auto,
    ReasoningMode mode = ReasoningMode.Balanced,
    string? model = null,
    params Tool[] tools)
        {
            tools = tools?.Any() == true ? tools : _tools.RegisteredTools.ToArray();
            if (tools.Length == 0)
                throw new ArgumentException("No tools registered.", nameof(tools));

            var resp = await _retryPolicy.ExecuteAsync(
                async intPrompt => await RunToolCallOnce(intPrompt, toolCallMode, mode, tools, model),
                prompt);

            return resp ?? new ToolCallResponse(Array.Empty<ToolCall>(), "No tool call produced.", null);
        }


        private async Task<ToolCallResponse> RunToolCallOnce(
            Conversation intPrompt,
            ToolCallMode toolCallMode,
            ReasoningMode mode,
            Tool[] tools,
            string? model = null)
        {
            var response = await _llm.GetToolCallResponse(intPrompt, tools, toolCallMode, mode, model);
            _tokenManager.Record(response.InputTokens ?? 0, response.OutputTokens ?? 0);
            var valid = new List<ToolCall>();
            var hadCalls = (response.ToolCalls?.Count ?? 0) > 0;
            foreach (var call in response.ToolCalls ?? Enumerable.Empty<ToolCall>())
            {
                if (_tools.Contains(call.Name))
                {
                    if (intPrompt.IsToolAlreadyCalled(call))
                    {
                        var lastResult = intPrompt.GetLastToolCallResult(call)!;
                        intPrompt.AddUser(
                            $"Tool `{call.Name}` was already called with the same arguments. " +
                            $"The result was: {lastResult?.AsPrettyJson()}. ");
                        continue;
                    }
                    valid.Add(new ToolCall(
                        call.Id ?? Guid.NewGuid().ToString(),
                        call.Name,
                        call.Arguments,
                        _parser.ParseToolParams(_tools, call.Name, call.Arguments)
                    ));
                }
            }

            if (valid.Count == 0 && !string.IsNullOrWhiteSpace(response.AssistantMessage))
            {
                if (intPrompt.IsLastAssistantMessageSame(response.AssistantMessage!))
                {
                    intPrompt.AddUser(
                        "You just gave the same assistant message. Don’t repeat — refine or add new info.");
                    return null!;
                }
                if (!hadCalls)
                {
                    // no tool calls at all — check for inline calls
                    var inline = _parser.TryExtractInlineToolCall(_tools, response.AssistantMessage);
                    if (inline != null)
                        valid.AddRange(inline.Calls);
                }
            }

            return new ToolCallResponse(
                valid,
                response.AssistantMessage,
                response.FinishReason
            );
        }

        public Task<IReadOnlyList<ToolCallResult>> RunToolCalls(List<ToolCall> toolCalls) =>
            _Runtime.HandleToolCallsAsync(toolCalls);
    }
}
