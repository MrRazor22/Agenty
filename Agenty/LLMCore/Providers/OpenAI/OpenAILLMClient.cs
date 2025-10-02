using Agenty.LLMCore.ChatHandling;
using Agenty.LLMCore.Messages;
using Agenty.LLMCore.ToolHandling;
using Newtonsoft.Json.Linq;
using OpenAI;
using OpenAI.Chat;
using System;
using System.ClientModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Agenty.LLMCore.Providers.OpenAI
{
    public class OpenAILLMClient : ILLMClient
    {
        private OpenAIClient? _client;
        private ChatClient? _chatClient;

        public void Initialize(string baseUrl, string apiKey, string modelName = "any_model")
        {
            _client = new OpenAIClient(
                credential: new ApiKeyCredential(apiKey),
                options: new OpenAIClientOptions { Endpoint = new Uri(baseUrl) }
            );

            _chatClient = _client.GetChatClient(modelName);
        }

        private void EnsureInitialized()
        {
            if (_client is null || _chatClient is null)
                throw new InvalidOperationException("Client not initialized. Call Initialize() first.");
        }

        public async Task<LLMResponse> GetResponse(Conversation prompt, LLMMode mode = LLMMode.Balanced)
        {
            EnsureInitialized();
            ChatCompletionOptions options = new ChatCompletionOptions() { ToolChoice = ChatToolChoice.CreateNoneChoice() };
            options.ApplyLLMMode(mode);

            var response = await _chatClient!.CompleteChatAsync(prompt.ToChatMessages(), options);

            string? text = string.Join("", response.Value.Content.Select(part => part.Text));
            return new LLMResponse(
                assistantMessage: string.IsNullOrWhiteSpace(text) ? null : text,
                finishReason: response.Value.FinishReason.ToString()
            );
        }

        public async IAsyncEnumerable<string> GetStreamingResponse(Conversation prompt, LLMMode mode = LLMMode.Balanced)
        {
            EnsureInitialized();
            ChatCompletionOptions options = new ChatCompletionOptions();
            options.ApplyLLMMode(mode);

            await foreach (var update in _chatClient!.CompleteChatStreamingAsync(prompt.ToChatMessages(), options))
            {
                foreach (var part in update.ContentUpdate)
                    yield return part.Text;
            }
        }

        public async Task<LLMResponse> GetToolCallResponse(
    Conversation prompt,
    IEnumerable<Tool> tools,
    ToolCallMode toolCallMode = ToolCallMode.Auto,
    LLMMode mode = LLMMode.Deterministic)
        {
            EnsureInitialized();

            var options = new ChatCompletionOptions
            {
                ToolChoice = toolCallMode.ToChatToolChoice()
            };
            options.ApplyLLMMode(mode);

            foreach (var t in tools.ToChatTools())
                options.Tools.Add(t);

            var response = await _chatClient!.CompleteChatAsync(prompt.ToChatMessages(), options);
            var result = response.Value;

            var toolCalls = new List<ToolCall>();
            if (result.ToolCalls != null && result.ToolCalls.Count > 0) // ✅ .NET Standard safe
            {
                foreach (var chatToolCall in result.ToolCalls)
                {
                    var name = chatToolCall.FunctionName;
                    var args = chatToolCall.FunctionArguments != null
    ? JObject.Parse(chatToolCall.FunctionArguments.ToString())
    : new JObject();

                    toolCalls.Add(new ToolCall(
                        chatToolCall.Id ?? Guid.NewGuid().ToString(),
                        name,
                        args
                    ));
                }

            }

            string content = (result.Content != null && result.Content.Count > 0)
                ? result.Content[0].Text
                : null;

            return new LLMResponse(
                assistantMessage: string.IsNullOrWhiteSpace(content) ? null : content,
                toolCalls: toolCalls,
                finishReason: result != null ? result.FinishReason.ToString() : null
            );
        }


        public async Task<LLMResponse> GetStructuredResponse(
    Conversation prompt,
    JObject responseFormat,
    LLMMode mode = LLMMode.Deterministic)
        {
            EnsureInitialized();

            var options = new ChatCompletionOptions
            {
                ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                    jsonSchemaFormatName: "structured_response",
                    jsonSchema: BinaryData.FromString(responseFormat.ToString(Newtonsoft.Json.Formatting.None)),
                    jsonSchemaIsStrict: true)
            };
            options.ApplyLLMMode(mode);

            var response = await _chatClient!.CompleteChatAsync(prompt.ToChatMessages(), options);
            var result = response.Value;

            var text = result.Content[0].Text;

            JObject structured = JObject.Parse(text);

            return new LLMResponse(
                structuredResult: structured,
                finishReason: result?.FinishReason.ToString()
            );
        }
    }
}
