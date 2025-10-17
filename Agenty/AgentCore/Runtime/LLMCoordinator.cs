using Agenty.AgentCore.TokenHandling;
using Agenty.LLMCore;
using Agenty.LLMCore.ChatHandling;
using Agenty.LLMCore.JsonSchema;
using Agenty.LLMCore.Messages;
using Agenty.LLMCore.ToolHandling;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Agenty.AgentCore.Runtime
{
    public class ToolCallResponse
    {
        public IReadOnlyList<ToolCall> Calls { get; }
        public string? AssistantMessage { get; }
        public string? FinishReason { get; }

        public ToolCallResponse(
            IReadOnlyList<ToolCall> calls,
            string? assistantMessage,
            string? finishReason)
        {
            Calls = calls;
            AssistantMessage = assistantMessage;
            FinishReason = finishReason;
        }
    }

    public interface ILLMCoordinator
    {
        Task<string?> GetResponse(Conversation prompt, ReasoningMode mode = ReasoningMode.Balanced, string? model = null);
        Task<T?> GetStructured<T>(Conversation prompt, ReasoningMode mode = ReasoningMode.Balanced, string? model = null) where T : class;
        Task<ToolCallResponse> GetToolCallResponse(Conversation prompt, ToolCallMode toolCallMode = ToolCallMode.Auto, ReasoningMode mode = ReasoningMode.Balanced, string? model = null, params Tool[] tools);
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
        private readonly ILogger<LLMCoordinator> _logger;

        private static readonly ConcurrentDictionary<string, JObject> _schemaCache = new ConcurrentDictionary<string, JObject>();

        public LLMCoordinator(
            ILLMClient llm,
            IToolCatalog registry,
            IToolRuntime Runtime,
            IToolCallParser parser,
            ITokenManager tokenManager,
            IRetryPolicy retryPolicy,
            ILogger<LLMCoordinator> logger)
        {
            _llm = llm;
            _tools = registry;
            _Runtime = Runtime;
            _parser = parser;
            _tokenManager = tokenManager;
            _retryPolicy = retryPolicy;
            _logger = logger;
        }

        public async Task<string?> GetResponse(Conversation prompt, ReasoningMode mode = ReasoningMode.Balanced, string? model = null)
        {
            _logger.LogInformation("Fetching simple LLM response (Mode={Mode}, Model={Model})", mode, model ?? "default");
            var resp = await _llm.GetResponse(prompt, mode, model);
            _tokenManager.Record(resp.InputTokens ?? 0, resp.OutputTokens ?? 0);
            _logger.LogDebug("Response received. Tokens In={In}, Out={Out}", resp.InputTokens, resp.OutputTokens);
            return resp.AssistantMessage;
        }

        public async Task<T?> GetStructured<T>(Conversation prompt, ReasoningMode mode = ReasoningMode.Deterministic, string? model = null) where T : class
        {
            _logger.LogInformation("Requesting structured response for {Type}", typeof(T).Name);
            return await _retryPolicy.ExecuteAsync(intPrompt => RunStructuredOnce<T>(intPrompt, mode, model), prompt);
        }

        private async Task<T?> RunStructuredOnce<T>(Conversation intPrompt, ReasoningMode mode, string? model = null) where T : class
        {
            var typeKey = typeof(T).FullName!;
            var schema = _schemaCache.GetOrAdd(typeKey, _ =>
            {
                _logger.LogDebug("Schema not cached. Building for {Type}", typeKey);
                return JsonSchemaExtensions.GetSchemaFor<T>();
            });

            _logger.LogDebug("Running structured call for {Type}", typeKey);
            var response = await _llm.GetStructuredResponse(intPrompt, schema, mode, model);
            _tokenManager.Record(response.InputTokens ?? 0, response.OutputTokens ?? 0);

            if (response?.StructuredResult == null)
            {
                _logger.LogWarning("Structured response for {Type} is null or empty", typeKey);
                intPrompt.AddAssistant($"Empty or invalid response. Return valid JSON for {typeof(T).Name}.");
                return default;
            }

            var errors = _parser.ValidateAgainstSchema(response.StructuredResult, schema, typeof(T).Name);
            if (errors.Any())
            {
                var msg = string.Join("; ", errors.Select(e => $"{e.Path}: {e.Message}"));
                _logger.LogWarning("Schema validation failed for {Type}: {Msg}", typeKey, msg);
                intPrompt.AddAssistant($"Validation failed: {msg}. Return valid JSON for {typeof(T).Name}.");
                return default;
            }

            _logger.LogDebug("Structured response for {Type} validated successfully", typeKey);
            return response.StructuredResult.ToObject<T>(JsonSerializer.Create(new JsonSerializerSettings
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

            _logger.LogInformation("Starting tool call detection (Mode={Mode}, Model={Model}, ToolCount={Count})",
                mode, model ?? "default", tools.Length);

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
            _logger.LogDebug("Running tool call pass (Mode={Mode}, Tools={Count})", mode, tools.Length);
            var response = await _llm.GetToolCallResponse(intPrompt, tools, toolCallMode, mode, model);
            _tokenManager.Record(response.InputTokens ?? 0, response.OutputTokens ?? 0);

            var valid = new List<ToolCall>();
            var hadCalls = (response.ToolCalls?.Count ?? 0) > 0;
            _logger.LogDebug("Received {Count} raw tool calls", response.ToolCalls?.Count ?? 0);

            foreach (var call in response.ToolCalls ?? Enumerable.Empty<ToolCall>())
            {
                if (!_tools.Contains(call.Name))
                {
                    _logger.LogWarning("LLM suggested unknown tool: {Tool}", call.Name);
                    intPrompt.AddUser($"Tool `{call.Name}` is invlaid, use only the available tools: [{tools.ToJoinedString()}] ");
                    continue;
                }

                if (call.ExistsIn(intPrompt, valid))
                {
                    _logger.LogWarning("Duplicate tool call detected: {Tool} (ignored)", call.Name);
                    var lastResult = intPrompt.GetLastToolCallResult(call)!;
                    intPrompt.AddUser($"Tool `{call.Name}` was already called with the same arguments. " +
                                      $"The result was: {lastResult?.AsPrettyJson()}.");
                    continue;
                }

                _logger.LogDebug("Accepting tool call: {Tool}", call.Name);
                valid.Add(new ToolCall(
                    call.Id ?? Guid.NewGuid().ToString(),
                    call.Name,
                    call.Arguments,
                    _parser.ParseToolParams(_tools, call.Name, call.Arguments)
                ));
            }

            if (valid.Count == 0 && !string.IsNullOrWhiteSpace(response.AssistantMessage))
            {
                if (intPrompt.IsLastAssistantMessageSame(response.AssistantMessage!))
                {
                    _logger.LogWarning("Repeated identical assistant message detected");
                    intPrompt.AddUser("You just gave the same assistant message. Don’t repeat — refine or add new info.");
                    return null!;
                }

                if (!hadCalls)
                {
                    // no tool calls at all — check for inline calls
                    var inline = _parser.TryExtractInlineToolCall(_tools, response.AssistantMessage);
                    if (inline != null)
                    {
                        _logger.LogInformation("Inline tool call detected from assistant message");
                        valid.AddRange(inline.Calls);
                    }
                }
            }

            _logger.LogInformation("Tool call phase complete. Valid calls={Count}", valid.Count);
            return new ToolCallResponse(valid, response.AssistantMessage, response.FinishReason);
        }

        public Task<IReadOnlyList<ToolCallResult>> RunToolCalls(List<ToolCall> toolCalls)
        {
            _logger.LogInformation("Executing {Count} tool calls", toolCalls.Count);
            return _Runtime.HandleToolCallsAsync(toolCalls);
        }
    }
}
