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
        // Plain response (text only, convenience)
        Task<string?> GetResponse(Conversation prompt, LLMMode mode = LLMMode.Balanced);

        // Structured JSON response (T must be a reference type)
        Task<T?> GetStructured<T>(Conversation prompt, LLMMode mode = LLMMode.Balanced)
            where T : class;

        // Tool calls (returns only what agent devs care about)
        Task<ToolCallResponse> GetToolCallResponse(
            Conversation prompt,
            ToolCallMode toolCallMode = ToolCallMode.Auto,
            LLMMode mode = LLMMode.Balanced,
            params Tool[] tools);

        // Tool execution
        Task<IReadOnlyList<ToolCallResult>> RunToolCalls(List<ToolCall> toolCalls);
    }


    internal sealed class LLMCoordinator : ILLMCoordinator
    {
        private readonly ILLMClient _llm;
        private readonly IToolRegistry _registry;
        private readonly IToolRuntime _Runtime;
        private readonly IToolCallParser _parser;
        private readonly IRetryPolicy _retryPolicy;

        public LLMCoordinator(
            ILLMClient llm,
            IToolRegistry registry,
            IToolRuntime Runtime,
            IToolCallParser parser,
            IRetryPolicy retryPolicy)
        {
            _llm = llm;
            _registry = registry;
            _Runtime = Runtime;
            _parser = parser;
            _retryPolicy = retryPolicy;
        }

        public async Task<string?> GetResponse(Conversation prompt, LLMMode mode = LLMMode.Balanced)
        {
            var resp = await _llm.GetResponse(prompt, mode);
            return resp.AssistantMessage;
        }

        public async Task<T?> GetStructured<T>(Conversation prompt, LLMMode mode = LLMMode.Deterministic) where T : class
        {
#if DEBUG
            return await RunStructuredOnce<T>(prompt, mode); // no retry, easy to step through
#else
    return await _retryPolicy.ExecuteAsync(intPrompt => RunOnce<T>(intPrompt, mode), prompt);
#endif
        }

        private async Task<T?> RunStructuredOnce<T>(Conversation intPrompt, LLMMode mode) where T : class
        {
            // build schema with Newtonsoft
            var schema = JsonSchemaExtensions.GetSchemaFor<T>(); // JObject
            var response = await _llm.GetStructuredResponse(intPrompt, schema, mode);

            if (response?.StructuredResult == null)
            {
                intPrompt.Add(Role.Assistant, $"Empty/invalid. Return valid JSON for {typeof(T).Name}.");
                return default;
            }

            var jsonNode = response.StructuredResult; // should be JToken/JObject
            var errors = _parser.ValidateAgainstSchema(jsonNode, schema, typeof(T).Name);

            if (errors.Any())
            {
                var msg = string.Join("; ", errors.Select(e => $"{e.Path}: {e.Message}"));
                intPrompt.Add(Role.Assistant, $"Validation failed: {msg}. Return valid JSON for {typeof(T).Name}.");
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
    LLMMode mode = LLMMode.Balanced,
    params Tool[] tools)
        {
            tools = tools?.Any() == true ? tools : _registry.RegisteredTools.ToArray();
            if (tools.Length == 0) throw new ArgumentException("No tools registered.", nameof(tools));

#if DEBUG
            return await RunToolCallOnce(prompt, toolCallMode, mode, tools);
#else
    var resp = await _retryPolicy.ExecuteAsync(
        intPrompt => RunToolCallOnce(intPrompt, toolCallMode, mode, tools),
        prompt);
    return resp ?? new ToolCallResponse(Array.Empty<ToolCall>(), "No tool call produced.", null);
#endif
        }

        private async Task<ToolCallResponse> RunToolCallOnce(
            Conversation intPrompt,
            ToolCallMode toolCallMode,
            LLMMode mode,
            Tool[] tools)
        {
            var response = await _llm.GetToolCallResponse(intPrompt, tools, toolCallMode, mode);

            var valid = new List<ToolCall>();
            foreach (var call in response.ToolCalls)
            {
                if (_registry.Contains(call.Name))
                {
                    if (intPrompt.IsToolAlreadyCalled(call))
                    {
                        string lastResult = intPrompt.GetLastToolCallResult(call);
                        intPrompt.Add(Role.User,
                            $"Tool `{call.Name}` was already called with the same arguments. " +
                            $"The result was: {lastResult}. ");
                        continue;
                    }
                    valid.Add(new ToolCall(
                        call.Id ?? Guid.NewGuid().ToString(),
                        call.Name,
                        call.Arguments,
                        _parser.ParseToolParams(_registry, call.Name, call.Arguments)
                    ));
                }
            }

            if (valid.Count == 0 && !string.IsNullOrWhiteSpace(response.AssistantMessage))
            {
                if (intPrompt.IsLastAssistantMessageSame(response.AssistantMessage))
                {
                    intPrompt.Add(Role.User,
                        "You just gave the same assistant message. Don’t repeat — refine or add new info.");
                    return null!;
                }
                var inline = _parser.TryExtractInlineToolCall(_registry, response.AssistantMessage);
                if (inline != null)
                    valid.AddRange(inline.Calls);
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
