using Agenty.LLMCore;
using Agenty.LLMCore.ChatHandling;
using Agenty.LLMCore.JsonSchema;
using Agenty.LLMCore.Messages;
using Agenty.LLMCore.ToolHandling;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Agenty.AgentCore.Runtime
{
    // Slimmed return type for tool calls
    public record ToolCallResponse(
        IReadOnlyList<ToolCall> Calls,
        string? AssistantMessage,
        string? FinishReason
    );

    public interface ILLMCoordinator
    {
        // Plain response (text only, convenience)
        Task<string?> GetResponse(Conversation prompt, LLMMode mode = LLMMode.Balanced);

        // Structured JSON response
        Task<T?> GetStructured<T>(Conversation prompt, LLMMode mode = LLMMode.Balanced);

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
        private readonly IToolRuntime _runtime;
        private readonly IToolCallParser _parser;
        private readonly IRetryPolicy _retryPolicy;

        public LLMCoordinator(
            ILLMClient llm,
            IToolRegistry registry,
            IToolRuntime runtime,
            IToolCallParser parser,
            IRetryPolicy retryPolicy)
        {
            _llm = llm;
            _registry = registry;
            _runtime = runtime;
            _parser = parser;
            _retryPolicy = retryPolicy;
        }

        public async Task<string?> GetResponse(Conversation prompt, LLMMode mode = LLMMode.Balanced)
        {
            var resp = await _llm.GetResponse(prompt, mode);
            return resp.AssistantMessage;
        }

        public async Task<T?> GetStructured<T>(Conversation prompt, LLMMode mode = LLMMode.Deterministic)
        {
            return await _retryPolicy.ExecuteAsync(async intPrompt =>
            {
                try
                {
                    var response = await _llm.GetStructuredResponse(
                        intPrompt,
                        JsonSchemaExtensions.GetSchemaFor<T>(),
                        mode);

                    if (response?.StructuredResult == null)
                    {
                        intPrompt.Add(Role.Assistant,
                            $"The last response was empty or invalid. Please return a valid JSON response for type {typeof(T).Name}.");
                        return default;
                    }

                    var jsonString = response.StructuredResult.ToJsonString();
                    var jsonOptions = new JsonSerializerOptions
                    {
                        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
                        PropertyNameCaseInsensitive = true
                    };
                    return JsonSerializer.Deserialize<T>(jsonString, jsonOptions);
                }
                catch (Exception ex)
                {
                    intPrompt.Add(Role.Assistant,
                        $"The last response failed with [{ex.Message}]. Please provide a valid JSON response matching the schema for {typeof(T).Name}.");
                    throw;
                }
            }, prompt);
        }

        public async Task<ToolCallResponse> GetToolCallResponse(
            Conversation prompt,
            ToolCallMode toolCallMode = ToolCallMode.Auto,
            LLMMode mode = LLMMode.Balanced,
            params Tool[] tools)
        {
            tools = tools?.Any() == true ? tools : _registry.RegisteredTools.ToArray();
            if (tools.Length == 0) throw new ArgumentException("No tools registered.", nameof(tools));

            var resp = await _retryPolicy.ExecuteAsync(async intPrompt =>
            {
                var response = await _llm.GetToolCallResponse(intPrompt, tools, toolCallMode, mode);

                var valid = new List<ToolCall>();
                foreach (var call in response.ToolCalls)
                {
                    if (_registry.Contains(call.Name))
                    {
                        if (prompt.IsToolAlreadyCalled(call))
                        {
                            string lastResult = prompt.GetLastToolCallResult(call);

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
                    if (prompt.IsLastAssistantMessageSame(response.AssistantMessage))
                    {
                        intPrompt.Add(Role.User,
                            "You just gave the same assistant message. Don’t repeat — refine or add new info.");
                        return null;
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
            }, prompt);

            return resp ?? new ToolCallResponse(Array.Empty<ToolCall>(), "No tool call produced.", null);
        }

        public Task<IReadOnlyList<ToolCallResult>> RunToolCalls(List<ToolCall> toolCalls) =>
            _runtime.HandleToolCallsAsync(toolCalls);
    }
}
