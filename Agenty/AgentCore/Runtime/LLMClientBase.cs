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
        protected abstract IAsyncEnumerable<LLMStreamChunk> StreamAsync(
            LLMRequestBase request,
            CancellationToken ct);
        #endregion

        public async Task<LLMStructuredResponse<T>> ExecuteAsync<T>(
            LLMStructuredRequest request,
            CancellationToken ct = default)
        {
            _logger.LogTrace("Requesting structured response for {Type}", typeof(T).Name);

            request.ResultType = typeof(T);

            string typeKey = request.ResultType.FullName;

            request.Schema = _schemaCache.GetOrAdd(
                typeKey,
                _ => JsonSchemaExtensions.GetSchemaForType(request.ResultType));

            request.AllowedTools =
                request.ToolCallMode == ToolCallMode.Disabled
                    ? new Tool[0]
                    : (request.AllowedTools != null && request.AllowedTools.Any()
                        ? request.AllowedTools.ToArray()
                        : _tools.RegisteredTools.ToArray());

            StringBuilder jsonBuffer = new StringBuilder();
            int input = 0;
            int output = 0;
            string finish = "stop";

            // ------------ STREAM WITH RETRIES ------------
            await foreach (var chunk in _retryPolicy.ExecuteStreamAsync(
                request,
                clonedRequest => StreamAsync(clonedRequest, ct),
                ct))
            {
                if (chunk.Kind == StreamKind.Text)
                    jsonBuffer.Append(chunk.AsText());

                if (chunk.Kind == StreamKind.Usage)
                {
                    if (chunk.InputTokens.HasValue) input = chunk.InputTokens.Value;
                    if (chunk.OutputTokens.HasValue) output = chunk.OutputTokens.Value;
                }

                if (chunk.Kind == StreamKind.Finish)
                {
                    if (chunk.FinishReason != null)
                        finish = chunk.FinishReason;
                }
            }

            _tokenManager.Record(input, output);

            string rawText = jsonBuffer.ToString();
            JToken json = null;

            try
            {
                json = JToken.Parse(rawText);
            }
            catch
            {
                // INVALID JSON → retry driver will see assistant correction
                _logger.LogWarning("Invalid JSON for structured response {Type}", typeKey);
                request.Prompt.AddAssistant("Return valid JSON matching the schema.");
                throw new Exception("Retry structured JSON parse");
            }

            // ---------- SCHEMA VALIDATION ----------
            var errors = _parser.ValidateAgainstSchema(json, request.Schema, request.ResultType.Name);

            if (errors.Count > 0)
            {
                var msg = string.Join("; ", errors.Select(e => e.Path + ": " + e.Message));
                _logger.LogWarning("Validation failed for {Type}: {Msg}", typeKey, msg);

                request.Prompt.AddAssistant("Validation failed: " + msg + ". Fix JSON.");
                throw new Exception("Retry structured validation");
            }

            T typed = json.ToObject<T>();

            return new LLMStructuredResponse<T>(
                json,
                typed,
                finish,
                input,
                output);
        }


        public async Task<LLMResponse> ExecuteAsync(
            LLMRequest? request,
            CancellationToken ct = default,
            Action<LLMStreamChunk>? onStream = null)
        {
            request.AllowedTools = request.ToolCallMode == ToolCallMode.Disabled
                ? Array.Empty<Tool>()
                : (request.AllowedTools?.Any() == true
                    ? request.AllowedTools.ToArray()
                    : _tools.RegisteredTools.ToArray());

            var sb = new StringBuilder();
            var toolCalls = new List<ToolCall>();

            int input = 0;
            int output = 0;
            string finish = "stop";

            // -----------------------------
            // MAIN STREAM (with retries)
            // -----------------------------
            await foreach (var chunk in _retryPolicy.ExecuteStreamAsync(
                request,
                clonedrequest => ProduceValidatedStream((LLMRequest)clonedrequest, ct),
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

            return new LLMResponse(
                finalText,
                toolCalls,
                finish,
                input,
                output
            );
        }
        private async IAsyncEnumerable<LLMStreamChunk> ProduceValidatedStream(LLMRequest request, [EnumeratorCancellation] CancellationToken ct)
        {
            bool limitOneTool = request.ToolCallMode == ToolCallMode.OneTool;
            await foreach (var rawChunk in StreamAsync(request, ct))
            {
                // TEXT ----------------------------------------------------
                if (rawChunk.Kind == StreamKind.Text)
                {
                    var text = rawChunk.AsText();
                    if (string.IsNullOrEmpty(text))
                        continue;

                    yield return rawChunk; // emit as-is

                    // inline extraction from assistant message
                    var extraction = _parser.TryExtractInlineToolCall(_tools, text);

                    foreach (var call in extraction.Calls)
                    {
                        if (!_tools.Contains(call.Name))
                        {
                            request.Prompt.AddUser(
                                $"Tool `{call.Name}` is invalid. Use only: [{string.Join(", ", _tools.RegisteredTools.Select(t => t.Name))}]"
                            );
                            continue;
                        }

                        var parsed = _parser.ParseToolParams(_tools, call.Name, call.Arguments);

                        var validated = new ToolCall(
                            call.Id ?? Guid.NewGuid().ToString(),
                            call.Name,
                            call.Arguments,
                            parsed
                        );

                        yield return new LLMStreamChunk(
                            StreamKind.ToolCall,
                            payload: validated
                        );

                        if (limitOneTool)
                            yield break;
                    }

                    continue;
                }

                // TOOLCALL ------------------------------------------------
                if (rawChunk.Kind == StreamKind.ToolCall)
                {
                    var raw = rawChunk.AsToolCall();
                    if (raw == null)
                        continue;

                    // UNKNOWN TOOL? -> correction prompt injection
                    if (!_tools.Contains(raw.Name))
                    {
                        request.Prompt.AddUser(
                            $"Tool `{raw.Name}` is invalid. Use only: [{string.Join(", ", _tools.RegisteredTools.Select(t => t.Name))}]"
                        );
                        continue; // don't emit bad call
                    }

                    // parse args
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
