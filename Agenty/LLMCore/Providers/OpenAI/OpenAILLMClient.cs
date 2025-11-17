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
    internal sealed class OpenAILLMClient : LLMClientBase
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

        protected override async IAsyncEnumerable<LLMStreamChunk> StreamAsync(
            LLMRequestBase request,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            var chat = GetChatClient(request.Model);

            var options = new ChatCompletionOptions();
            options.ApplySamplingOptions(request);

            // 1. Structured output strict mode
            bool isStructured = request is LLMStructuredRequest sr;
            if (isStructured)
            {
                var sreq = (LLMStructuredRequest)request;

                options.ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                    jsonSchemaFormatName: "structured_response",
                    jsonSchema: BinaryData.FromString(
                        sreq.Schema.ToString(Newtonsoft.Json.Formatting.None)
                    ),
                    jsonSchemaIsStrict: true
                );
            }

            // 2. Tools if present
            if (request is LLMRequest toolReq)
            {
                options.ToolChoice = toolReq.ToolCallMode.ToChatToolChoice();
                options.AllowParallelToolCalls = false;

                foreach (var t in toolReq.AllowedTools.ToChatTools())
                    options.Tools.Add(t);
            }

            var stream = chat.CompleteChatStreamingAsync(
                request.Prompt.ToChatMessages(),
                options,
                ct
            );

            // Tool assembly state
            string? toolId = null;
            string? toolName = null;
            var toolArgsSb = new StringBuilder();

            // Structured mode buffer
            StringBuilder? jsonBuffer = isStructured ? new StringBuilder() : null;

            // Usage & finish
            int inputTokens = 0;
            int outputTokens = 0;
            string? finishReason = null;

            await foreach (var update in stream.WithCancellation(ct))
            {
                // === TOKEN USAGE ===
                if (update.Usage != null)
                {
                    inputTokens = update.Usage.InputTokenCount;
                    outputTokens = update.Usage.OutputTokenCount;
                }

                // === TEXT (also contains structured output JSON if in that mode) ===
                if (update.ContentUpdate != null)
                {
                    foreach (var c in update.ContentUpdate)
                    {
                        if (c.Text != null)
                        {
                            var text = c.Text;

                            // Emit text chunk ALWAYS
                            yield return new LLMStreamChunk(
                                StreamKind.Text,
                                payload: text
                            );

                            // Also collect into JSON buffer if structured
                            if (isStructured)
                                jsonBuffer!.Append(text);
                        }
                    }
                }

                // === TOOL CALL FRAGMENTS ===
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

                // === TOOL CALL ASSEMBLED ===
                if (toolArgsSb.Length > 0 &&
                    toolArgsSb.ToString().TryParseCompleteJson(out _))
                {
                    JObject args;
                    try { args = JObject.Parse(toolArgsSb.ToString()); }
                    catch { args = new JObject(); }

                    yield return new LLMStreamChunk(
                        StreamKind.ToolCall,
                        new ToolCall(toolId!, toolName!, args)
                    );

                    toolArgsSb.Clear();
                    toolId = null;
                    toolName = null;
                }

                // === FINISH REASON ===
                if (update.FinishReason != null)
                    finishReason = update.FinishReason.Value.ToString();
            }

            // === USAGE ===
            yield return new LLMStreamChunk(
                StreamKind.Usage,
                input: inputTokens,
                output: outputTokens
            );

            // === FINISH ===
            yield return new LLMStreamChunk(
                StreamKind.Finish,
                finish: finishReason ?? "stop",
                payload: isStructured ? jsonBuffer?.ToString() : null
            );
        }

    }
}
