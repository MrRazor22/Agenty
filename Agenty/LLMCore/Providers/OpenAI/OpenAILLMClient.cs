using Agenty.LLMCore.ChatHandling;
using Agenty.LLMCore.ToolHandling;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using System.Text.Json;
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
                options: new OpenAIClientOptions()
                {
                    Endpoint = new Uri(baseUrl)
                }
            );

            _chatClient = _client.GetChatClient(modelName);
        }
        private void EnsureInitialized()
        {
            if (_client is null || _chatClient is null)
                throw new InvalidOperationException("Client not initialized. Call Initialize() first.");
        }

        public async Task<string> GetResponse(Conversation prompt, LLMMode mode = LLMMode.Balanced)
        {
            EnsureInitialized();
            ChatCompletionOptions options = new() { ToolChoice = ChatToolChoice.CreateNoneChoice() };
            options.ApplyAgentMode(mode);

            var response = await _chatClient!.CompleteChatAsync(prompt.ToChatMessages(), options);

            var contentParts = response.Value.Content;
            var textContent = string.Join("", contentParts.Select(part => part.Text));
            return textContent;
        }

        public async IAsyncEnumerable<string> GetStreamingResponse(Conversation prompt, LLMMode mode = LLMMode.Balanced)
        {
            EnsureInitialized();
            ChatCompletionOptions options = new();
            options.ApplyAgentMode(mode);

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

            List<ChatTool> chatTools = tools.ToChatTools();

            ChatCompletionOptions options = new()
            {
                ToolChoice = toolCallMode.ToChatToolChoice()
            };
            options.ApplyAgentMode(mode);

            chatTools.ForEach(t => options.Tools.Add(t));

            var response = await _chatClient!.CompleteChatAsync(prompt.ToChatMessages(), options);
            var result = response.Value;

            var toolCalls = new List<ToolCall>();
            if (result?.ToolCalls != null && result.ToolCalls.Any())
            {
                foreach (var chatToolCall in result.ToolCalls)
                {
                    if (tools.Any(t => t.Name.Equals(chatToolCall.FunctionName, StringComparison.InvariantCultureIgnoreCase)))
                    {
                        var name = chatToolCall.FunctionName;
                        var args = chatToolCall.FunctionArguments.ToObjectFromJson<JsonObject>() ?? new JsonObject();
                        toolCalls.Add(new
                        (
                            chatToolCall.Id ?? Guid.NewGuid().ToString(),
                            name,
                            args,
                            null,
                            string.Empty
                        ));
                    }
                }
            }

            string? content = result?.Content?.FirstOrDefault()?.Text;
            string? finishReason = result?.FinishReason.ToString();

            return new LLMResponse
            {
                AssistantMessage = string.IsNullOrWhiteSpace(content) ? null : content,
                ToolCalls = toolCalls,
                FinishReason = finishReason
            };
        }

        public async Task<JsonNode> GetStructuredResponse(Conversation prompt, JsonObject responseFormat, LLMMode mode = LLMMode.Deterministic)
        {
            EnsureInitialized();

            ChatCompletionOptions options = new()
            {
                ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                    jsonSchemaFormatName: "structured_response",
                    jsonSchema: BinaryData.FromString(responseFormat.ToJsonString()),
                    jsonSchemaIsStrict: true)
            };
            options.ApplyAgentMode(mode);

            ChatCompletion completion = await _chatClient!.CompleteChatAsync(prompt.ToChatMessages(), options);

            using JsonDocument structuredJson = JsonDocument.Parse(completion.Content[0].Text);
            return JsonNode.Parse(structuredJson.RootElement.GetRawText())!;
        }
    }
}
