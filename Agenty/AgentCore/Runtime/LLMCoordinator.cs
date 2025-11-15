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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Agenty.AgentCore.Runtime
{
    public interface ILLMCoordinator
    {
        Task<LLMTextToolCallResult> GetResponse(
            Conversation prompt,
            ToolCallMode toolCallMode = ToolCallMode.Auto,
            ReasoningMode mode = ReasoningMode.Balanced,
            string? model = null,
            LLMCallOptions? opts = null,
            CancellationToken ct = default,
            params Tool[] tools);
        Task<T> GetStructured<T>(
            Conversation prompt,
            ReasoningMode mode = ReasoningMode.Deterministic,
            string? model = null,
            LLMCallOptions? opts = null,
            CancellationToken ct = default,
            ToolCallMode toolCallMode = ToolCallMode.None,
            params Tool[] tools);

        IAsyncEnumerable<LLMStreamChunk> StreamResponse(
            Conversation prompt,
            ToolCallMode toolCallMode = ToolCallMode.Auto,
            ReasoningMode mode = ReasoningMode.Balanced,
            string? model = null,
            LLMCallOptions? opts = null,
            CancellationToken ct = default,
            params Tool[] tools);

        Task<IReadOnlyList<ToolCallResult>> RunToolCalls(
            List<ToolCall> toolCalls,
            CancellationToken ct = default);
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
        public async Task<LLMTextToolCallResult> GetResponse(
            Conversation prompt,
            ToolCallMode toolCallMode = ToolCallMode.Auto,
            ReasoningMode mode = ReasoningMode.Balanced,
            string? model = null,
            LLMCallOptions? opts = null,
            CancellationToken ct = default,
            params Tool[] tools)
        {
            tools = (tools.Length > 0) ? tools : _tools.RegisteredTools.ToArray();

            var sb = new System.Text.StringBuilder();
            var toolCalls = new List<ToolCall>();

            int input = 0;
            int output = 0;
            string finish = "stop";

            await foreach (var chunk in StreamResponse(
                prompt,
                toolCallMode,
                mode,
                model,
                opts,
                ct,
                tools))
            {
                if (ct.IsCancellationRequested)
                    break;

                switch (chunk.Kind)
                {
                    case StreamKind.Text:
                        var text = chunk.AsText();
                        if (!string.IsNullOrEmpty(text))
                            sb.Append(text);
                        break;

                    case StreamKind.ToolCall:
                        var tc = chunk.AsToolCall();
                        if (tc != null)
                            toolCalls.Add(tc);
                        break;

                    case StreamKind.Usage:
                        input = chunk.InputTokens ?? input;
                        output = chunk.OutputTokens ?? output;
                        break;

                    case StreamKind.Finish:
                        finish = chunk.FinishReason ?? finish;
                        break;
                }
            }

            _tokenManager.Record(input, output);

            var answer = sb.ToString();
            if (!string.IsNullOrWhiteSpace(answer))
                prompt.AddAssistant(answer);

            return new LLMTextToolCallResult(
                assistantMessage: answer,
                toolCalls: toolCalls,
                finishReason: finish,
                input: input,
                output: output
            );
        }

        public async Task<T> GetStructured<T>(
    Conversation prompt,
    ReasoningMode mode = ReasoningMode.Deterministic,
    string? model = null,
    LLMCallOptions? opts = null,
    CancellationToken ct = default,
    ToolCallMode toolCallMode = ToolCallMode.None,
    params Tool[] tools)
        {
            _logger.LogTrace("Requesting structured response for {Type}", typeof(T).Name);

            return await _retryPolicy.ExecuteAsync(async intPrompt =>
            {
                var typeKey = typeof(T).FullName!;
                var schema = _schemaCache.GetOrAdd(typeKey, _ =>
                {
                    _logger.LogTrace("Schema not cached. Building for {Type}", typeKey);
                    return JsonSchemaExtensions.GetSchemaFor<T>();
                });

                // call llm client (internal result)
                var result = await _llm.GetStructuredResponse(
                    intPrompt,
                    schema,
                    mode,
                    model,
                    opts,
                    ct,
                    toolCallMode,
                    tools);

                _tokenManager.Record(result.InputTokens, result.OutputTokens);

                // no payload
                if (result.Payload == null)
                {
                    _logger.LogWarning("Structured response for {Type} was null", typeKey);
                    intPrompt.AddAssistant($"Return proper JSON for {typeof(T).Name}.");
                    return default;
                }

                var token = result.Payload as JToken;
                if (token == null)
                {
                    _logger.LogWarning("Structured payload for {Type} is not JToken", typeKey);
                    intPrompt.AddAssistant($"Return valid JSON object for {typeof(T).Name}.");
                    return default;
                }

                // validate against schema
                var errors = _parser.ValidateAgainstSchema(token, schema, typeof(T).Name);
                if (errors.Count > 0)
                {
                    var msg = string.Join("; ", errors.Select(e => $"{e.Path}: {e.Message}"));
                    _logger.LogWarning("Schema validation failed for {Type}: {Msg}", typeKey, msg);

                    intPrompt.AddAssistant($"Validation failed: {msg}. Fix JSON for {typeof(T).Name}.");

                    return default;
                }

                // final conversion
                try
                {
                    var settings = new JsonSerializerSettings
                    {
                        Converters = { new Newtonsoft.Json.Converters.StringEnumConverter() }
                    };

                    return token.ToObject<T>(JsonSerializer.Create(settings));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed converting structured JSON to {Type}", typeof(T).Name);
                    intPrompt.AddAssistant($"JSON parsed but cannot convert to {typeof(T).Name}. Fix it.");
                    return default;
                }

            }, prompt);
        }

        public async IAsyncEnumerable<LLMStreamChunk> StreamResponse(
            Conversation prompt,
            ToolCallMode toolCallMode = ToolCallMode.Auto,
            ReasoningMode mode = ReasoningMode.Balanced,
            string? model = null,
            LLMCallOptions? opts = null,
            [EnumeratorCancellation] CancellationToken ct = default,
            params Tool[] tools)
        {
            tools = (tools.Length > 0) ? tools : _tools.RegisteredTools.ToArray();

            await foreach (var chunk in _retryPolicy.ExecuteStreamAsync(
                () => _llm.GetResponseStreaming(prompt, tools, toolCallMode, mode, model, opts, ct),
                ct))
            {
                switch (chunk.Kind)
                {
                    case StreamKind.Text:
                        {
                            // forward original text chunk
                            yield return chunk;

                            var text = chunk.AsText();
                            if (string.IsNullOrEmpty(text))
                                break;

                            // inline extraction
                            var extraction = _parser.TryExtractInlineToolCall(_tools, text);

                            if (extraction.Calls.Count > 0)
                            {
                                foreach (var c in extraction.Calls)
                                {
                                    object[] parsedParams;
                                    try
                                    {
                                        parsedParams = _parser.ParseToolParams(_tools, c.Name, c.Arguments);
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogWarning(ex, "Inline param parse failed for {Tool}", c.Name);
                                        parsedParams = Array.Empty<object>();
                                    }

                                    var call = new ToolCall(
                                        id: c.Id ?? Guid.NewGuid().ToString(),
                                        name: c.Name,
                                        arguments: c.Arguments,
                                        parameters: parsedParams
                                    );

                                    var inlineChunk = new LLMStreamChunk(
                                        StreamKind.ToolCall,
                                        payload: call
                                    );

                                    HandleToolCallMode(toolCallMode, tools, inlineChunk, prompt);

                                    yield return inlineChunk;

                                    if (toolCallMode == ToolCallMode.OneTool)
                                        yield break;
                                }
                            }

                            break;
                        }

                    case StreamKind.ToolCall:
                        {
                            var raw = chunk.AsToolCall();
                            if (raw == null)
                                break;

                            object[] parsedParams;
                            try
                            {
                                parsedParams = _parser.ParseToolParams(_tools, raw.Name, raw.Arguments);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Direct tool param parse failed for {Tool}", raw.Name);
                                parsedParams = Array.Empty<object>();
                            }

                            var enriched = new ToolCall(
                                id: raw.Id ?? Guid.NewGuid().ToString(),
                                name: raw.Name,
                                arguments: raw.Arguments,
                                parameters: parsedParams
                            );

                            var enrichedChunk = new LLMStreamChunk(
                                StreamKind.ToolCall,
                                payload: enriched
                            );

                            HandleToolCallMode(toolCallMode, tools, enrichedChunk, prompt);

                            yield return enrichedChunk;

                            if (toolCallMode == ToolCallMode.OneTool)
                                yield break;

                            break;
                        }

                    case StreamKind.Usage:
                        _tokenManager.Record(chunk.InputTokens ?? 0, chunk.OutputTokens ?? 0);
                        yield return chunk;
                        break;

                    case StreamKind.Finish:
                        yield return chunk;
                        break;
                }
            }
        }
        private void HandleToolCallMode(
            ToolCallMode mode,
            Tool[] allowedTools,
            LLMStreamChunk chunk,
            Conversation prompt)
        {
            var call = chunk.AsToolCall();
            if (call == null)
                return;

            var name = call.Name;

            if (!_tools.Contains(name))
            {
                prompt.AddAssistant(
                    $"Tool `{name}` is invalid. Use: [{string.Join(", ", allowedTools.Select(t => t.Name))}]");
                return;
            }

            if (mode == ToolCallMode.None)
            {
                prompt.AddAssistant($"Tool call `{name}` not allowed in ToolCallMode.None.");
                return;
            }

            if (mode == ToolCallMode.Required)
                return;

            if (mode == ToolCallMode.Auto)
                return;

            if (mode == ToolCallMode.OneTool)
                return;
        }

        public Task<IReadOnlyList<ToolCallResult>> RunToolCalls(List<ToolCall> toolCalls, CancellationToken ct = default)
        {
            _logger.LogTrace("Executing {Count} tool calls", toolCalls.Count);
            return _Runtime.HandleToolCallsAsync(toolCalls, ct);
        }
    }
}
