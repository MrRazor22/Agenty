using Agenty.LLMCore.ChatHandling;
using Agenty.LLMCore.Messages;
using Agenty.LLMCore.ToolHandling;
using Newtonsoft.Json.Linq;
using OpenAI;
using OpenAI.Chat;
using System;
using System.ClientModel;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Agenty.LLMCore.Providers.OpenAI
{
    internal sealed class OpenAILLMClient : ILLMClient
    {
        private OpenAIClient? _client;
        private readonly ConcurrentDictionary<string, ChatClient> _chatClients =
            new ConcurrentDictionary<string, ChatClient>(StringComparer.OrdinalIgnoreCase);

        private string? _defaultModel;

        public void Initialize(string baseUrl, string apiKey, string modelName = "gpt-4o")
        {
            _client = new OpenAIClient(
                credential: new ApiKeyCredential(apiKey),
                options: new OpenAIClientOptions { Endpoint = new Uri(baseUrl) }
            );

            _defaultModel = modelName;
            _chatClients[modelName] = _client.GetChatClient(modelName);
        }

        private ChatClient GetChatClient(string? model = null)
        {
            if (_client == null)
                throw new InvalidOperationException("Client not initialized. Call Initialize() first.");

            var key = model ?? _defaultModel ?? throw new InvalidOperationException("Model not specified.");
            return _chatClients.GetOrAdd(key, m => _client.GetChatClient(m));
        }

        public async Task<LLMResponse> GetResponse(
            Conversation prompt,
            ReasoningMode mode = ReasoningMode.Balanced,
            string? model = null, CancellationToken ct = default)
        {
            var chat = GetChatClient(model);
            var options = new ChatCompletionOptions { ToolChoice = ChatToolChoice.CreateNoneChoice() };
            options.ApplyLLMMode(mode);

            var response = await chat.CompleteChatAsync(prompt.ToChatMessages(), options, ct);
            var result = response.Value;
            var text = string.Join("", result.Content.Select(c => c.Text));

            return new LLMResponse(
                assistantMessage: string.IsNullOrWhiteSpace(text) ? null : text,
                finishReason: result.FinishReason.ToString())
            {
                InputTokens = result.Usage?.InputTokenCount,
                OutputTokens = result.Usage?.OutputTokenCount
            };
        }

        public async IAsyncEnumerable<string> GetStreamingResponse(
            Conversation prompt,
            ReasoningMode mode = ReasoningMode.Balanced,
            string? model = null, CancellationToken ct = default)
        {
            var chat = GetChatClient(model);
            var options = new ChatCompletionOptions();
            options.ApplyLLMMode(mode);

            await foreach (var update in chat.CompleteChatStreamingAsync(prompt.ToChatMessages(), options, ct))
            {
                foreach (var part in update.ContentUpdate)
                    yield return part.Text;
            }
        }

        public async Task<LLMResponse> GetToolCallResponse(
            Conversation prompt,
            IEnumerable<Tool> tools,
            ToolCallMode toolCallMode = ToolCallMode.Auto,
            ReasoningMode mode = ReasoningMode.Deterministic,
            string? model = null, CancellationToken ct = default)
        {
            // Use streaming cutoff only for OneTool mode
            if (toolCallMode == ToolCallMode.OneTool)
            {
                return await GetSingleToolCallStreaming(prompt, tools, mode, model);
            }

            var chat = GetChatClient(model);
            var options = new ChatCompletionOptions { ToolChoice = toolCallMode.ToChatToolChoice() };
            options.ApplyLLMMode(mode);

            foreach (var t in tools.ToChatTools())
                options.Tools.Add(t);

            var response = await chat.CompleteChatAsync(prompt.ToChatMessages(), options, ct);
            var result = response.Value;

            var toolCalls = new List<ToolCall>();
            if (result.ToolCalls != null)
            {
                foreach (var call in result.ToolCalls)
                {
                    var args = call.FunctionArguments != null
                        ? JObject.Parse(call.FunctionArguments.ToString())
                        : new JObject();

                    toolCalls.Add(new ToolCall(call.Id ?? Guid.NewGuid().ToString(), call.FunctionName, args));
                }
            }

            var content = result.Content?.FirstOrDefault()?.Text;

            return new LLMResponse(
                assistantMessage: string.IsNullOrWhiteSpace(content) ? null : content,
                toolCalls: toolCalls,
                finishReason: result.FinishReason.ToString())
            {
                InputTokens = result.Usage?.InputTokenCount,
                OutputTokens = result.Usage?.OutputTokenCount
            };
        }
        private async Task<LLMResponse> GetSingleToolCallStreaming(
    Conversation prompt,
    IEnumerable<Tool> tools,
    ReasoningMode mode,
    string? model, CancellationToken ct = default)
        {
            var chat = GetChatClient(model);
            var options = new ChatCompletionOptions
            {
                ToolChoice = ChatToolChoice.CreateAutoChoice(),
                AllowParallelToolCalls = false
            };
            options.ApplyLLMMode(mode);

            foreach (var t in tools.ToChatTools())
                options.Tools.Add(t);

            var streamingResponse = chat.CompleteChatStreamingAsync(prompt.ToChatMessages(), options, ct);

            string? toolCallId = null;
            string? toolName = null;
            var toolArgs = new System.Text.StringBuilder();
            var content = new System.Text.StringBuilder();
            int inputTokens = 0, outputTokens = 0;
            string? finishReason = null;

            await foreach (var update in streamingResponse)
            {
                if (update.Usage != null)
                {
                    inputTokens = update.Usage.InputTokenCount;
                    outputTokens = update.Usage.OutputTokenCount;
                }
                if (update.ToolCallUpdates != null)
                {
                    foreach (var toolUpdate in update.ToolCallUpdates)
                    {
                        toolCallId ??= toolUpdate.ToolCallId;
                        toolName ??= toolUpdate.FunctionName;
                        var argsDelta = toolUpdate.FunctionArgumentsUpdate?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(argsDelta))
                            toolArgs.Append(argsDelta);
                    }
                }
                if (update.ContentUpdate != null)
                {
                    foreach (var contentItem in update.ContentUpdate)
                    {
                        if (contentItem.Text != null)
                            content.Append(contentItem.Text);
                    }
                }

                if (update.FinishReason != null)
                    finishReason = update.FinishReason.Value.ToString();

                if (toolArgs.Length > 0 && toolArgs.ToString().TryParseCompleteJson(out _))
                {
                    finishReason ??= "tool_calls";
                    break;
                }
            }

            var toolCalls = new List<ToolCall>();
            if (toolCallId != null && toolName != null)
            {
                try
                {
                    var args = JObject.Parse(toolArgs.ToString());
                    toolCalls.Add(new ToolCall(toolCallId, toolName, args));
                }
                catch
                {
                    toolCalls.Add(new ToolCall(toolCallId, toolName, new JObject()));
                }
            }

            return new LLMResponse(
                assistantMessage: content.Length > 0 ? content.ToString() : null,
                toolCalls: toolCalls,
                finishReason: finishReason ?? "stop")
            {
                InputTokens = inputTokens,
                OutputTokens = outputTokens
            };
        }
        public async Task<LLMResponse> GetStructuredResponse(
            Conversation prompt,
            JObject responseFormat,
            ReasoningMode mode = ReasoningMode.Deterministic,
            string? model = null, CancellationToken ct = default)
        {
            var chat = GetChatClient(model);
            var options = new ChatCompletionOptions
            {
                ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                    jsonSchemaFormatName: "structured_response",
                    jsonSchema: BinaryData.FromString(responseFormat.ToString(Newtonsoft.Json.Formatting.None)),
                    jsonSchemaIsStrict: true)
            };
            options.ApplyLLMMode(mode);

            var response = await chat.CompleteChatAsync(prompt.ToChatMessages(), options, ct);
            var result = response.Value;

            var text = result.Content[0].Text;
            var structured = JObject.Parse(text);

            return new LLMResponse(
                structuredResult: structured,
                finishReason: result.FinishReason.ToString())
            {
                InputTokens = result.Usage?.InputTokenCount,
                OutputTokens = result.Usage?.OutputTokenCount
            };
        }
    }
}
