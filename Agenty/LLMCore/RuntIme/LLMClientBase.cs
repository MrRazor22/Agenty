using Agenty.AgentCore.TokenHandling;
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

namespace Agenty.LLMCore.Runtime
{
    public abstract class LLMClientBase : ILLMClient
    {
        private int _currInTokensSoFar;
        private int _currOutTokensSoFar;
        private string _currFinishReason;
        private readonly IToolCatalog _tools;
        private readonly IToolRuntime _Runtime;
        private readonly IToolCallParser _parser;
        private readonly ITokenizer _tokenizer;
        private readonly IContextTrimmer _trimmer;
        private readonly ITokenManager _tokenManager;
        private readonly IRetryPolicy _retryPolicy;
        private readonly ILogger<ILLMClient> _logger;
        protected string BaseUrl { get; }
        protected string ApiKey { get; }
        protected string DefaultModel { get; }


        private static readonly ConcurrentDictionary<string, JObject> _schemaCache = new ConcurrentDictionary<string, JObject>();

        public LLMClientBase(
            LLMInitOptions opts,
            IToolCatalog registry,
            IToolRuntime runtime,
            IToolCallParser parser,
            ITokenizer tokenizer,
            IContextTrimmer trimmer,
            ITokenManager tokenManager,
            IRetryPolicy retryPolicy,
            ILogger<ILLMClient> logger)
        {
            BaseUrl = opts.BaseUrl;
            ApiKey = opts.ApiKey;
            DefaultModel = opts.Model;

            _tools = registry;
            _Runtime = runtime;
            _parser = parser;
            _tokenizer = tokenizer;
            _trimmer = trimmer;
            _tokenManager = tokenManager;
            _retryPolicy = retryPolicy;
            _logger = logger;
        }

        #region abstract methods providers must implement 
        protected abstract IAsyncEnumerable<LLMStreamChunk> StreamAsync(
            LLMRequestBase request,
            CancellationToken ct);
        #endregion

        private async IAsyncEnumerable<LLMStreamChunk> PrepareStreamAsync(
            LLMRequestBase request,
            [EnumeratorCancellation] CancellationToken ct)
        {
            request.Prompt = _trimmer.Trim(request.Prompt, null, request.Model ?? DefaultModel);

            _currInTokensSoFar = 0;
            _currOutTokensSoFar = 0;
            _currFinishReason = "stop";

            var sb = new StringBuilder();
            var liveLog = new StringBuilder();   // <--- NEW

            if (_logger.IsEnabled(LogLevel.Trace))
            {
                _logger.LogTrace("► Outbound Messages:\n{Json}",
                    JsonConvert.SerializeObject(request.Prompt.ToLogList(), Formatting.Indented));
            }

            await foreach (var chunk in StreamAsync(request, ct))
            {
                if (chunk.Kind == StreamKind.Text)
                {
                    var txt = chunk.AsText();
                    sb.Append(txt);
                    liveLog.Append(txt);

                    // 🔥 single rolling log, no chunk flood
                    if (_logger.IsEnabled(LogLevel.Trace))
                        _logger.LogTrace("◄ Inbound Stream: {Text}", liveLog.ToString());
                }

                if (chunk.Kind == StreamKind.Usage)
                {
                    if (chunk.InputTokens.HasValue)
                        _currInTokensSoFar = chunk.InputTokens.Value;

                    if (chunk.OutputTokens.HasValue)
                        _currOutTokensSoFar = chunk.OutputTokens.Value;
                }

                if (chunk.Kind == StreamKind.Finish)
                {
                    if (chunk.FinishReason != null)
                        _currFinishReason = chunk.FinishReason ?? _currFinishReason;
                }

                yield return chunk;
            }

            // fallback
            int input = _currInTokensSoFar;
            if (input <= 0)
                input = _tokenizer.Count(request.Prompt.ToJson(ChatFilter.All), request.Model ?? DefaultModel);

            int output = _currOutTokensSoFar;
            if (output <= 0)
                output = _tokenizer.Count(sb.ToString(), request.Model ?? DefaultModel);

            _tokenManager.Record(input, output);
            _logger.LogTrace("LLM Call Tokens: in={In}, out={Out}", input, output);
        }

        public async Task<LLMStructuredResponse<T>> ExecuteAsync<T>(
            LLMStructuredRequest request,
            CancellationToken ct = default,
            Action<LLMStreamChunk>? onStream = null)
        {
            _logger.LogDebug("Structured request start: {Type}", typeof(T).Name);

            request.ResultType = typeof(T);

            string typeKey = request.ResultType.FullName;

            request.Schema = _schemaCache.GetOrAdd(
                typeKey,
                _ => request.ResultType.GetSchemaForType());

            request.AllowedTools =
                request.ToolCallMode == ToolCallMode.Disabled
                    ? new Tool[0]
                    : request.AllowedTools != null && request.AllowedTools.Any()
                        ? request.AllowedTools.ToArray()
                        : _tools.RegisteredTools.ToArray();

            StringBuilder jsonBuffer = new StringBuilder();

            // ------------ STREAM WITH RETRIES ------------
            await foreach (var chunk in _retryPolicy.ExecuteStreamAsync(
                request,
                clonedRequest => PrepareStreamAsync(clonedRequest, ct),
                ct))
            {
                onStream?.Invoke(chunk);

                if (chunk.Kind == StreamKind.Text)
                    jsonBuffer.Append(chunk.AsText());
            }

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
                throw new RetryException("Return valid JSON matching the schema.");
            }

            // ---------- SCHEMA VALIDATION ----------
            var errors = _parser.ValidateAgainstSchema(json, request.Schema, request.ResultType.Name);

            if (errors.Count > 0)
            {
                var msg = string.Join("; ", errors.Select(e => e.Path + ": " + e.Message));
                _logger.LogWarning("Validation failed for {Type}: {Msg}", typeKey, msg);

                throw new RetryException("Validation failed: " + msg + ". Fix JSON.");
            }

            T typed = json.ToObject<T>();

            _logger.LogDebug("Structured request completed: {Type}", typeof(T).Name);
            return new LLMStructuredResponse<T>(
                json,
                typed,
                _currFinishReason,
                _currInTokensSoFar,
                _currOutTokensSoFar
            );
        }


        public async Task<LLMResponse> ExecuteAsync(
            LLMRequest? request,
            CancellationToken ct = default,
            Action<LLMStreamChunk>? onStream = null)
        {
            _logger.LogDebug("LLM request start");

            request.AllowedTools = request.ToolCallMode == ToolCallMode.Disabled
                ? Array.Empty<Tool>()
                : request.AllowedTools?.Any() == true
                    ? request.AllowedTools.ToArray()
                    : _tools.RegisteredTools.ToArray();

            var sb = new StringBuilder();
            var toolCalls = new List<ToolCall>();

            // -----------------------------
            // MAIN STREAM (with retries)
            // -----------------------------
            await foreach (var chunk in _retryPolicy.ExecuteStreamAsync(
                request,
                clonedrequest => ValidateToolCallsAndStream((LLMRequest)clonedrequest, ct),
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
                        _logger.LogInformation("Tool call received: {Name}", chunk.AsToolCall()?.Name);
                        break;
                }
            }

            var finalText = sb.ToString().Trim();

            _logger.LogDebug("LLM request completed");
            return new LLMResponse(
                finalText,
                toolCalls,
                _currFinishReason,
                _currInTokensSoFar,
                _currOutTokensSoFar
            );
        }
        private async IAsyncEnumerable<LLMStreamChunk> ValidateToolCallsAndStream(LLMRequest request, [EnumeratorCancellation] CancellationToken ct)
        {
            var currentStreamCalls = new List<ToolCall>();
            bool limitOneTool = request.ToolCallMode == ToolCallMode.OneTool;
            await foreach (var rawChunk in PrepareStreamAsync(request, ct))
            {
                // TEXT ----------------------------------------------------
                if (rawChunk.Kind == StreamKind.Text)
                {
                    var text = rawChunk.AsText();
                    if (string.IsNullOrEmpty(text)) continue;

                    // repeated assistant message check (only once per stream) 
                    CheckRepeatAssistantMessage(request.Prompt, text);

                    yield return rawChunk; // emit as-is

                    // inline extraction from assistant message
                    var extraction = _parser.TryExtractInlineToolCall(_tools, text);

                    foreach (var call in extraction.Calls)
                    {
                        var validated = TryParseToolCalls(request.Prompt, call);
                        if (validated == null) continue;

                        CheckRepeatToolCall(request.Prompt, validated, currentStreamCalls);

                        currentStreamCalls.Add(validated);

                        yield return new LLMStreamChunk(
                            StreamKind.ToolCall,
                            payload: validated
                        );

                        if (limitOneTool) yield break;
                    }

                    continue;
                }

                // TOOLCALL ------------------------------------------------
                if (rawChunk.Kind == StreamKind.ToolCall)
                {
                    var raw = rawChunk.AsToolCall();
                    if (raw == null) continue;

                    var validated = TryParseToolCalls(request.Prompt, raw);
                    if (validated == null) continue;

                    CheckRepeatToolCall(request.Prompt, validated, currentStreamCalls);

                    currentStreamCalls.Add(validated);

                    yield return new LLMStreamChunk(
                        StreamKind.ToolCall,
                        payload: validated
                    );

                    if (limitOneTool) yield break;

                    continue;
                }

                // USAGE / FINISH ------------------------------------------
                yield return rawChunk;
            }
        }

        private void CheckRepeatAssistantMessage(Conversation requestPrompt, string? text)
        {
            if (requestPrompt.IsLastAssistantMessageSame(text))
            {
                _logger.LogWarning("Assistant repeated same message");

                throw new RetryException(
                    "You repeated the same assistant response. Don't repeat — refine or add new info."
                );
            }
        }

        private void CheckRepeatToolCall(Conversation requestPrompt, ToolCall validated, List<ToolCall> currentStreamTools)
        {
            if (validated.ExistsIn(requestPrompt, currentStreamTools))
            {
                _logger.LogWarning("Duplicate tool call ignored: {Tool}", validated.Name);

                var lastResult = requestPrompt.GetLastToolCallResult(validated);
                throw new RetryException(
                    $"Tool `{validated.Name}` was already called with same arguments. " +
                    $"Last result: {lastResult?.AsPrettyJson() ?? "null"}"
                );
            }
        }

        private ToolCall? TryParseToolCalls(Conversation reqPrompt, ToolCall raw)
        {
            if (!_tools.Contains(raw.Name))
            {
                _logger.LogWarning("Invalid tool: {Name}", raw.Name);
                throw new RetryException(
                    $"Tool `{raw.Name}` is invalid. Use one of: {string.Join(", ", _tools.RegisteredTools.Select(t => t.Name))}."
                );
            }

            var parsed = _parser.ParseToolParams(_tools, raw.Name, raw.Arguments);

            return new ToolCall(
                raw.Id ?? Guid.NewGuid().ToString(),
                raw.Name,
                raw.Arguments,
                parsed
            );
        }
    }
}
