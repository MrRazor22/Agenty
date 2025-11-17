using Agenty.AgentCore.TokenHandling;
using Agenty.LLMCore;
using Agenty.LLMCore.ChatHandling;
using Agenty.LLMCore.JsonSchema;
using Agenty.LLMCore.Messages;
using Agenty.LLMCore.ToolHandling;
using Microsoft.Extensions.Logging;
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
    public abstract class LLMClientBase : ILLMClient
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

        public LLMClientBase(
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
        protected abstract IAsyncEnumerable<LLMStreamChunk> GetProviderStreamingResponse(
            LLMToolRequest request,
            CancellationToken ct);
        protected abstract Task<LLMStructuredResponse> GetProviderStructuredResponse(
            LLMStructuredRequest request,
            CancellationToken ct);
        #endregion

        public async Task<LLMStructuredResponse> ExecuteAsync(
            LLMStructuredRequest request,
            CancellationToken ct = default)
        {
            _logger.LogTrace("Requesting structured response for {Type}", request.ResultType.Name);

            return await _retryPolicy.ExecuteAsync(async intPrompt =>
            {
                var typeKey = request.ResultType.FullName!;
                request.Schema = _schemaCache.GetOrAdd(typeKey, _ =>
                {
                    _logger.LogTrace("Schema not cached. Building for {Type}", typeKey);
                    return JsonSchemaExtensions.GetSchemaForType(request.ResultType);
                });

                request.AllowedTools = request.ToolCallMode == ToolCallMode.Disabled
                    ? Array.Empty<Tool>()
                    : request.AllowedTools?.ToArray() ?? _tools.RegisteredTools.ToArray();

                var result = await GetProviderStructuredResponse(request, ct);

                _tokenManager.Record(result.InputTokens, result.OutputTokens);

                if (result.Payload == null)
                {
                    _logger.LogWarning("Structured response for {Type} was null", typeKey);
                    intPrompt.AddAssistant($"Return proper JSON for {request.ResultType.Name}.");
                    return result;
                }

                var token = result.Payload as JToken;
                if (token == null)
                {
                    _logger.LogWarning("Payload for {Type} is not JToken", typeKey);
                    intPrompt.AddAssistant($"Return valid JSON object for {request.ResultType.Name}.");
                    return result;
                }

                var errors = _parser.ValidateAgainstSchema(token, request.Schema, request.ResultType.Name);
                if (errors.Count > 0)
                {
                    var msg = string.Join("; ", errors.Select(e => $"{e.Path}: {e.Message}"));
                    _logger.LogWarning("Schema validation failed for {Type}: {Msg}", typeKey, msg);

                    intPrompt.AddAssistant($"Validation failed: {msg}. Fix JSON for {request.ResultType.Name}.");

                    return result;
                }

                return result;

            }, request.Prompt);
        }

        public async Task<LLMToolCallResponse> ExecuteAsync(
            LLMToolRequest? request,
            CancellationToken ct = default,
            Action<LLMStreamChunk>? onStream = null)
        {
            request.AllowedTools = request.ToolCallMode == ToolCallMode.Disabled
                    ? Array.Empty<Tool>()
                    : request.AllowedTools?.ToArray() ?? _tools.RegisteredTools.ToArray();

            var sb = new StringBuilder();
            var toolCalls = new List<ToolCall>();

            int input = 0;
            int output = 0;
            string finish = "stop";

            // -----------------------------
            // MAIN STREAM (with retries)
            // -----------------------------
            await foreach (var chunk in _retryPolicy.ExecuteStreamAsync(
                request.Prompt,
                clonedPrompt => ProduceValidatedStream(request, ct),
                ct))
            {
                onStream?.Invoke(chunk);

                switch (chunk.Kind)
                {
                    case StreamKind.Text:
                        sb.Append(chunk.AsText());
                        break;

                    case StreamKind.ToolCall:
                        toolCalls.Add(chunk.AsToolCall());
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

            var finalText = sb.ToString().Trim();

            return new LLMToolCallResponse(
                finalText,
                toolCalls,
                finish,
                input,
                output
            );
        }
        private async IAsyncEnumerable<LLMStreamChunk> ProduceValidatedStream(LLMToolRequest request, [EnumeratorCancellation] CancellationToken ct)
        {
            bool limitOneTool = request.ToolCallMode == ToolCallMode.OneTool;
            await foreach (var rawChunk in GetProviderStreamingResponse(request, ct))
            {
                // TEXT ----------------------------------------------------
                if (rawChunk.Kind == StreamKind.Text)
                {
                    var text = rawChunk.AsText();
                    if (!string.IsNullOrEmpty(text))
                    {
                        // first yield text
                        yield return rawChunk;

                        // inline extraction
                        var extraction = _parser.TryExtractInlineToolCall(_tools, text);
                        foreach (var c in extraction.Calls)
                        {
                            if (!_tools.Contains(c.Name))
                                throw new Exception($"Unknown tool '{c.Name}'");

                            var parsed = _parser.ParseToolParams(_tools, c.Name, c.Arguments);

                            var validated = new ToolCall(
                                c.Id ?? Guid.NewGuid().ToString(),
                                c.Name,
                                c.Arguments,
                                parsed
                            );

                            // yield validated toolcall
                            yield return new LLMStreamChunk(
                                StreamKind.ToolCall,
                                payload: validated
                            );

                            if (limitOneTool)
                                yield break;
                        }
                    }
                    continue;
                }

                // TOOLCALL ------------------------------------------------
                if (rawChunk.Kind == StreamKind.ToolCall)
                {
                    var raw = rawChunk.AsToolCall();
                    if (raw == null)
                        continue;


                    if (!_tools.Contains(raw.Name))
                        throw new Exception($"Unknown tool '{raw.Name}'");

                    var parsed = _parser.ParseToolParams(_tools, raw.Name, raw.Arguments);

                    var validated = new ToolCall(
                        raw.Id ?? Guid.NewGuid().ToString(),
                        raw.Name,
                        raw.Arguments,
                        parsed
                    );

                    yield return new LLMStreamChunk(
                        StreamKind.ToolCall,
                        payload: validated
                    );

                    if (limitOneTool)
                        yield break;

                    continue;
                }

                // USAGE / FINISH ------------------------------------------
                yield return rawChunk;
            }
        }

        public Task<IReadOnlyList<ToolCallResult>> RunToolCalls(List<ToolCall> toolCalls, CancellationToken ct = default)
        {
            _logger.LogTrace("Executing {Count} tool calls", toolCalls.Count);
            return _Runtime.HandleToolCallsAsync(toolCalls, ct);
        }
    }
}
