using Agenty.AgentCore.TokenHandling;
using Agenty.LLMCore.ChatHandling;
using Agenty.LLMCore.Messages;
using Agenty.LLMCore.Runtime;
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
            LLMInitOptions opts,
            IToolCatalog registry,
            IToolRuntime runtime,
            IToolCallParser parser,
            ITokenizer tokenizer,
            IContextTrimmer trimmer,
            ITokenManager tokenManager,
            IRetryPolicy retryPolicy,
            ILogger<ILLMClient> logger
        ) : base(opts, registry, runtime, parser, tokenizer, trimmer, tokenManager, retryPolicy, logger)
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

            // unified init
            bool isStructured = false;
            string? toolId = null;
            string? toolName = null;
            StringBuilder toolArgsSb = new StringBuilder();
            StringBuilder? jsonBuffer = null;

            // -------------------------------------------
            // ONE switch to configure EVERYTHING
            // -------------------------------------------
            switch (request)
            {
                case LLMStructuredRequest sreq:
                    isStructured = true;

                    // structured config
                    options.ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                        "structured_response",
                        BinaryData.FromString(
                            sreq.Schema.ToString(Newtonsoft.Json.Formatting.None)
                        ),
                        jsonSchemaIsStrict: true
                    );

                    // ALSO init json buffer
                    jsonBuffer = new StringBuilder();

                    // ALSO tools if allowed (Structured may allow tools)
                    if (sreq.AllowedTools != null)
                    {
                        options.ToolChoice = sreq.ToolCallMode.ToChatToolChoice();
                        options.AllowParallelToolCalls = false;

                        foreach (var t in sreq.AllowedTools.ToChatTools())
                            options.Tools.Add(t);
                    }
                    break;

                case LLMRequest toolReq:
                    // tool-only request
                    options.ToolChoice = toolReq.ToolCallMode.ToChatToolChoice();
                    options.AllowParallelToolCalls = false;

                    foreach (var t in toolReq.AllowedTools.ToChatTools())
                        options.Tools.Add(t);

                    break;

                default:
                    // text-only request: nothing needed
                    break;
            }


            var stream = chat.CompleteChatStreamingAsync(
                request.Prompt.ToChatMessages(),
                options,
                ct
            );

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
