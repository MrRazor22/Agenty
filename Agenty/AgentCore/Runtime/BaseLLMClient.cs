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
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Agenty.AgentCore.Runtime
{
    public abstract class BaseLLMClient : ILLMClient
    {
        private readonly IToolCatalog _tools;
        private readonly IToolRuntime _Runtime;
        private readonly IToolCallParser _parser;
        private readonly ITokenManager _tokenManager;
        private readonly IRetryPolicy _retryPolicy;
        private readonly ILogger<ILLMClient> _logger;
        protected string BaseUrl { get; }
        protected string ApiKey { get; }
        protected string DefaultModel { get; }


        private static readonly ConcurrentDictionary<string, JObject> _schemaCache = new ConcurrentDictionary<string, JObject>();

        public BaseLLMClient(
            string baseUrl,
            string apiKey,
            string defaultModel,
            IToolCatalog registry,
            IToolRuntime Runtime,
            IToolCallParser parser,
            ITokenManager tokenManager,
            IRetryPolicy retryPolicy,
            ILogger<ILLMClient> logger)
        {
            BaseUrl = baseUrl;
            ApiKey = apiKey;
            DefaultModel = defaultModel;
            _tools = registry;
            _Runtime = Runtime;
            _parser = parser;
            _tokenManager = tokenManager;
            _retryPolicy = retryPolicy;
            _logger = logger;
        }

        #region abstract methods providers must implement
        protected abstract IAsyncEnumerable<LLMStreamChunk> ProviderStream(
            Conversation prompt,
            IEnumerable<Tool> tools,
            ToolCallMode toolCallMode,
            ReasoningMode mode,
            string? model,
            LLMCallOptions? opts,
            CancellationToken ct);

        protected abstract Task<LLMStructuredResult> ProviderStructured(
            Conversation prompt,
            JObject responseFormat,
            ReasoningMode mode,
            ToolCallMode toolCallMode,
            string? model,
            LLMCallOptions? opts,
            CancellationToken ct,
            params Tool[] tools);
        #endregion 
        public async Task<T> GetStructuredTyped<T>(
            Conversation prompt,
            ToolCallMode toolCallMode = ToolCallMode.None,
            ReasoningMode mode = ReasoningMode.Deterministic,
            string model = null,
            LLMCallOptions opts = null,
            CancellationToken ct = default,
            params Tool[] tools)
        {
            var result = await GetStructured(
                prompt,
                typeof(T),
                toolCallMode,
                mode,
                model,
                opts,
                ct,
                tools);

            var token = result.Payload as JToken;
            if (token == null)
                return default(T);

            try
            {
                return token.ToObject<T>();
            }
            catch
            {
                return default(T);
            }
        }

        public async Task<LLMStructuredResult> GetStructured(
            Conversation prompt,
            Type targetType,
            ToolCallMode toolCallMode = ToolCallMode.None,
            ReasoningMode mode = ReasoningMode.Deterministic,
            string? model = null,
            LLMCallOptions? opts = null,
            CancellationToken ct = default,
            params Tool[] tools)
        {
            _logger.LogTrace("Requesting structured response for {Type}", targetType.Name);

            return await _retryPolicy.ExecuteAsync(async intPrompt =>
            {
                var typeKey = targetType.FullName!;
                var schema = _schemaCache.GetOrAdd(typeKey, _ =>
                {
                    _logger.LogTrace("Schema not cached. Building for {Type}", typeKey);
                    return JsonSchemaExtensions.GetSchemaForType(targetType);
                });

                var result = await ProviderStructured(
                    intPrompt,
                    schema,
                    mode,
                    toolCallMode,
                    model,
                    opts,
                    ct,
                    tools);

                _tokenManager.Record(result.InputTokens, result.OutputTokens);

                if (result.Payload == null)
                {
                    _logger.LogWarning("Structured response for {Type} was null", typeKey);
                    intPrompt.AddAssistant($"Return proper JSON for {targetType.Name}.");
                    return result;
                }

                var token = result.Payload as JToken;
                if (token == null)
                {
                    _logger.LogWarning("Payload for {Type} is not JToken", typeKey);
                    intPrompt.AddAssistant($"Return valid JSON object for {targetType.Name}.");
                    return result;
                }

                var errors = _parser.ValidateAgainstSchema(token, schema, targetType.Name);
                if (errors.Count > 0)
                {
                    var msg = string.Join("; ", errors.Select(e => $"{e.Path}: {e.Message}"));
                    _logger.LogWarning("Schema validation failed for {Type}: {Msg}", typeKey, msg);

                    intPrompt.AddAssistant($"Validation failed: {msg}. Fix JSON for {targetType.Name}.");

                    return result;
                }

                return result;

            }, prompt);
        }

        public async Task<LLMTextToolCallResult> GetResponseStreaming(
            Conversation prompt,
            ToolCallMode toolCallMode = ToolCallMode.Auto,
            ReasoningMode mode = ReasoningMode.Balanced,
            string? model = null,
            LLMCallOptions? opts = null,
            CancellationToken ct = default,
            Action<LLMStreamChunk>? onChunk = null,
            params Tool[] tools)
        {
            tools = (tools.Length > 0) ? tools : _tools.RegisteredTools.ToArray();

            bool limitOne = toolCallMode == ToolCallMode.OneTool;

            var sb = new StringBuilder();
            var toolCalls = new List<ToolCall>();

            int input = 0;
            int output = 0;
            string finish = "stop";

            await foreach (var chunk in _retryPolicy.ExecuteStreamAsync(
                prompt,
                p => ProviderStream(p, tools, toolCallMode, mode, model, opts, ct),
                ct))
            {
                onChunk?.Invoke(chunk);

                switch (chunk.Kind)
                {
                    case StreamKind.Text:
                        {
                            var text = chunk.AsText();
                            if (!string.IsNullOrEmpty(text))
                                sb.Append(text);

                            // INLINE
                            var extraction = _parser.TryExtractInlineToolCall(_tools, text);
                            foreach (var c in extraction.Calls)
                            {
                                if (limitOne && toolCalls.Count > 0)
                                    break;

                                if (!_tools.Contains(c.Name))
                                {
                                    prompt.AddAssistant(
                                        $"Tool `{c.Name}` is invalid. Use only: {string.Join(", ", tools.Select(t => t.Name))}.");
                                    continue;
                                }

                                try
                                {
                                    var parsed = _parser.ParseToolParams(_tools, c.Name, c.Arguments);
                                    toolCalls.Add(new ToolCall(
                                        c.Id ?? Guid.NewGuid().ToString(),
                                        c.Name,
                                        c.Arguments,
                                        parsed));
                                }
                                catch
                                {
                                    prompt.AddAssistant($"Fix parameters for tool `{c.Name}`.");
                                }
                            }
                            break;
                        }

                    case StreamKind.ToolCall:
                        {
                            var raw = chunk.AsToolCall();
                            if (raw == null) break;

                            if (limitOne && toolCalls.Count > 0)
                                break;

                            if (!_tools.Contains(raw.Name))
                            {
                                prompt.AddAssistant(
                                    $"Tool `{raw.Name}` is invalid. Allowed: {string.Join(", ", tools.Select(t => t.Name))}.");
                                break;
                            }

                            try
                            {
                                var parsed = _parser.ParseToolParams(_tools, raw.Name, raw.Arguments);
                                toolCalls.Add(new ToolCall(
                                    raw.Id ?? Guid.NewGuid().ToString(),
                                    raw.Name,
                                    raw.Arguments,
                                    parsed));
                            }
                            catch
                            {
                                prompt.AddAssistant($"Fix parameters for tool `{raw.Name}`.");
                            }
                            break;
                        }

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

            var finalText = sb.ToString().Trim();
            if (!string.IsNullOrEmpty(finalText))
                prompt.AddAssistant(finalText);

            return new LLMTextToolCallResult(
                finalText,
                toolCalls,
                finish,
                input,
                output
            );
        }

        public Task<IReadOnlyList<ToolCallResult>> RunToolCalls(List<ToolCall> toolCalls, CancellationToken ct = default)
        {
            _logger.LogTrace("Executing {Count} tool calls", toolCalls.Count);
            return _Runtime.HandleToolCallsAsync(toolCalls, ct);
        }
    }
}
