using Agenty.AgentCore.Runtime;
using Agenty.AgentCore.TokenHandling;
using Agenty.LLMCore.ChatHandling;
using Agenty.LLMCore.Messages;
using Agenty.LLMCore.ToolHandling;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using OpenAI;
using OpenAI.Chat;
using System;
using System.ClientModel;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Agenty.LLMCore.Providers.OpenAI
{
    internal sealed class OpenAILLMClient : BaseLLMClient
    {
        private OpenAIClient? _client;
        private readonly ConcurrentDictionary<string, ChatClient> _chatClients =
            new ConcurrentDictionary<string, ChatClient>(StringComparer.OrdinalIgnoreCase);

        private string? _defaultModel;
        public OpenAILLMClient(
            string baseUrl,
            string apiKey,
            string modelName,
            IToolCatalog registry,
            IToolRuntime runtime,
            IToolCallParser parser,
            ITokenManager tokenManager,
            IRetryPolicy retryPolicy,
            ILogger<ILLMClient> logger
        ) : base(baseUrl, apiKey, modelName, registry, runtime, parser, tokenManager, retryPolicy, logger)
        {
            _client = new OpenAIClient(
                credential: new ApiKeyCredential(ApiKey),
                options: new OpenAIClientOptions { Endpoint = new Uri(BaseUrl) }
            );

            _defaultModel = DefaultModel;
            _chatClients[_defaultModel] = _client.GetChatClient(_defaultModel);
        }

        private ChatClient GetChatClient(string? model = null)
        {
            if (_client == null)
                throw new InvalidOperationException("Client not initialized. Call Initialize() first.");

            var key = model ?? _defaultModel ?? throw new InvalidOperationException("Model not specified.");
            return _chatClients.GetOrAdd(key, m => _client.GetChatClient(m));
        }

        protected override async IAsyncEnumerable<LLMStreamChunk> ProviderStream(
            Conversation prompt,
            IEnumerable<Tool> tools,
            ToolCallMode toolCallMode = ToolCallMode.Auto,
            ReasoningMode mode = ReasoningMode.Deterministic,
            string? model = null,
            LLMCallOptions? opts = null,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            var chat = GetChatClient(model);

            var options = new ChatCompletionOptions
            {
                ToolChoice = toolCallMode.ToChatToolChoice(),
                AllowParallelToolCalls = false
            };

            options.ApplyLLMMode(mode);
            if (opts?.Temperature != null) options.Temperature = opts.Temperature;
            if (opts?.TopP != null) options.TopP = opts.TopP;
            if (opts?.MaxOutputTokens != null) options.MaxOutputTokenCount = opts.MaxOutputTokens;

            foreach (var t in tools.ToChatTools())
                options.Tools.Add(t);

            var stream = chat.CompleteChatStreamingAsync(prompt.ToChatMessages(), options, ct);

            string? toolId = null;
            string? toolName = null;
            var toolArgsSb = new StringBuilder();

            int inputTokens = 0;
            int outputTokens = 0;
            string? finishReason = null;

            await foreach (var update in stream.WithCancellation(ct))
            {
                if (update.Usage != null)
                {
                    inputTokens = update.Usage.InputTokenCount;
                    outputTokens = update.Usage.OutputTokenCount;
                }

                // TEXT
                if (update.ContentUpdate != null)
                {
                    foreach (var c in update.ContentUpdate)
                    {
                        if (c.Text != null)
                        {
                            yield return new LLMStreamChunk(
                                StreamKind.Text,
                                payload: c.Text
                            );
                        }
                    }
                }

                // TOOLCALL fragments
                if (update.ToolCallUpdates != null)
                {
                    foreach (var tcu in update.ToolCallUpdates)
                    {
                        toolId ??= tcu.ToolCallId;
                        toolName ??= tcu.FunctionName;

                        var delta = tcu.FunctionArgumentsUpdate?.ToString();
                        if (!string.IsNullOrEmpty(delta))
                            toolArgsSb.Append(delta);
                    }
                }

                // TOOLCALL assembled
                if (toolArgsSb.Length > 0 && toolArgsSb.ToString().TryParseCompleteJson(out _))
                {
                    JObject args;
                    try { args = JObject.Parse(toolArgsSb.ToString()); }
                    catch { args = new JObject(); }

                    var call = new ToolCall(toolId!, toolName!, args);

                    yield return new LLMStreamChunk(
                        StreamKind.ToolCall,
                        payload: call
                    );

                    toolArgsSb.Clear();
                    toolId = null;
                    toolName = null;
                }

                if (update.FinishReason != null)
                    finishReason = update.FinishReason.Value.ToString();
            }

            // USAGE
            yield return new LLMStreamChunk(
                StreamKind.Usage,
                payload: null,
                input: inputTokens,
                output: outputTokens
            );

            // FINISH
            yield return new LLMStreamChunk(
                StreamKind.Finish,
                payload: null,
                finish: finishReason ?? "stop"
            );
        }


        protected override async Task<LLMStructuredResult> ProviderStructured(
            Conversation prompt,
            JObject responseFormat,
            ReasoningMode mode = ReasoningMode.Deterministic,
            string model = null,
            LLMCallOptions opts = null,
            CancellationToken ct = default,
            ToolCallMode toolCallMode = ToolCallMode.None,
            params Tool[] tools)
        {
            var chat = GetChatClient(model);

            var options = new ChatCompletionOptions
            {
                ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                    jsonSchemaFormatName: "structured_response",
                    jsonSchema: BinaryData.FromString(
                        responseFormat.ToString(Newtonsoft.Json.Formatting.None)
                    ),
                    jsonSchemaIsStrict: true)
            };

            // tool mode
            options.ToolChoice = toolCallMode.ToChatToolChoice();

            // attach tools
            if (tools != null && tools.Length > 0)
            {
                foreach (var t in tools.ToChatTools())
                    options.Tools.Add(t);
            }

            options.ApplyLLMMode(mode);

            if (opts?.Temperature != null) options.Temperature = opts.Temperature;
            if (opts?.TopP != null) options.TopP = opts.TopP;
            if (opts?.MaxOutputTokens != null) options.MaxOutputTokenCount = opts.MaxOutputTokens;

            // call
            var response = await chat.CompleteChatAsync(prompt.ToChatMessages(), options, ct);
            var result = response.Value;

            // raw structured JSON
            var raw = result.Content?[0]?.Text?.Trim();
            JToken payload = null;

            if (!string.IsNullOrEmpty(raw))
            {
                try
                {
                    payload = JToken.Parse(raw);
                }
                catch
                {
                    // fallback: allow quoted raw strings
                    if (raw.StartsWith("\"") && raw.EndsWith("\""))
                        payload = JValue.Parse(raw);
                    else
                        payload = JValue.CreateString(raw);
                }
            }

            return new LLMStructuredResult(
                payload,
                result.FinishReason.ToString(),
                result.Usage?.InputTokenCount ?? 0,
                result.Usage?.OutputTokenCount ?? 0
            );
        }
    }
}
