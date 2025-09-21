using Agenty.LLMCore.ChatHandling;
using Agenty.LLMCore.Messages;
using Agenty.LLMCore.ToolHandling;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using System.Text.Json.Nodes;

namespace Agenty.LLMCore.Providers.OpenAI
{
    public class OpenAILLMClient() : ILLMClient
    {
        private OpenAIClient? _client;
        private ChatClient? _chatClient;

        public void Initialize(string baseUrl, string apiKey, string modelName = "any_model")
        {
            _client = new(
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
            ChatCompletionOptions options = new() { ToolChoice = ChatToolChoice.CreateNoneChoice() };
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
            ChatCompletionOptions options = new();
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
            if (result.ToolCalls is { Count: > 0 })
            {
                foreach (var chatToolCall in result.ToolCalls)
                {
                    var name = chatToolCall.FunctionName;
                    var args = chatToolCall.FunctionArguments.ToObjectFromJson<JsonObject>() ?? new JsonObject();

                    toolCalls.Add(new ToolCall(
                        chatToolCall.Id ?? Guid.NewGuid().ToString(),
                        name,
                        args
                    ));
                }
            }

            string? content = result?.Content?.FirstOrDefault()?.Text;
            return new LLMResponse(
                assistantMessage: string.IsNullOrWhiteSpace(content) ? null : content,
                toolCalls: toolCalls,
                finishReason: result?.FinishReason.ToString()
            );
        }

        public async Task<LLMResponse> GetStructuredResponse(
             Conversation prompt,
             JsonObject responseFormat,
             LLMMode mode = LLMMode.Deterministic)
        {
            EnsureInitialized();

            var options = new ChatCompletionOptions
            {
                ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                    jsonSchemaFormatName: "structured_response",
                    jsonSchema: BinaryData.FromString(responseFormat.ToJsonString()),
                    jsonSchemaIsStrict: true)
            };
            options.ApplyLLMMode(mode);

            var response = await _chatClient!.CompleteChatAsync(prompt.ToChatMessages(), options);
            var result = response.Value;

            var text = result.Content[0].Text;

            JsonNode structured = JsonNode.Parse(text)!;

            return new LLMResponse(
                structuredResult: structured,
                finishReason: result?.FinishReason.ToString()
            );
        }
    }
}
