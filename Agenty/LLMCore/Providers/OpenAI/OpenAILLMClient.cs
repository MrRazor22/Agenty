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
            string? model = null)
        {
            var chat = GetChatClient(model);
            var options = new ChatCompletionOptions { ToolChoice = ChatToolChoice.CreateNoneChoice() };
            options.ApplyLLMMode(mode);

            var response = await chat.CompleteChatAsync(prompt.ToChatMessages(), options);
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
            string? model = null)
        {
            var chat = GetChatClient(model);
            var options = new ChatCompletionOptions();
            options.ApplyLLMMode(mode);

            await foreach (var update in chat.CompleteChatStreamingAsync(prompt.ToChatMessages(), options))
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
            string? model = null)
        {
            var chat = GetChatClient(model);
            var options = new ChatCompletionOptions { ToolChoice = toolCallMode.ToChatToolChoice() };
            options.ApplyLLMMode(mode);

            foreach (var t in tools.ToChatTools())
                options.Tools.Add(t);

            var response = await chat.CompleteChatAsync(prompt.ToChatMessages(), options);
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

        public async Task<LLMResponse> GetStructuredResponse(
            Conversation prompt,
            JObject responseFormat,
            ReasoningMode mode = ReasoningMode.Deterministic,
            string? model = null)
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

            var response = await chat.CompleteChatAsync(prompt.ToChatMessages(), options);
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
