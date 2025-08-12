using Agenty.LLMCore.JsonSchema;
using HtmlAgilityPack;
using Microsoft.VisualStudio.TestPlatform.CommunicationUtilities;
using OpenAI;
using OpenAI.Chat;
using System;
using System.ClientModel;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

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

        public async Task<string> GetResponse(Conversations prompt)
        {
            EnsureInitialized();
            var response = await _chatClient!.CompleteChatAsync(prompt.ToChatMessages());

            var contentParts = response.Value.Content;
            var textContent = string.Join("", contentParts.Select(part => part.Text));
            return textContent;
        }

        public async IAsyncEnumerable<string> GetStreamingResponse(Conversations prompt)
        {
            EnsureInitialized();

            AsyncCollectionResult<StreamingChatCompletionUpdate> responseUpdates = _chatClient!.CompleteChatStreamingAsync(prompt.ToChatMessages());
            await foreach (var update in responseUpdates)
            {
                foreach (var part in update.ContentUpdate)
                {
                    yield return part.Text;
                }
            }
        }
        public async Task<ToolCall> GetToolCallResponse(Conversations prompt, IEnumerable<Tool> tools, bool forceToolCall = false)
        {
            EnsureInitialized();

            List<ChatTool> chatTools = tools.ToChatTools();

            ChatCompletionOptions options = new() { ToolChoice = forceToolCall ? ChatToolChoice.CreateRequiredChoice() : ChatToolChoice.CreateAutoChoice() };

            chatTools.ForEach(t => options.Tools.Add(t));

            var response = await _chatClient!.CompleteChatAsync(prompt.ToChatMessages(), options);
            var result = response.Value;

            var chatToolCall = result?.ToolCalls?.FirstOrDefault();
            if (chatToolCall != null)
            {
                if (tools.Any(t => t.Name.Equals(chatToolCall.FunctionName, StringComparison.InvariantCultureIgnoreCase)))
                {
                    var name = chatToolCall.FunctionName;
                    var args = chatToolCall.FunctionArguments.ToObjectFromJson<JsonObject>() ?? new JsonObject();
                    return new
                    (
                        chatToolCall.Id ?? Guid.NewGuid().ToString(),
                        name,
                        args,
                        null,
                        result?.Content?.FirstOrDefault()?.Text
                    );
                }
            }

            string? content = result?.Content?.FirstOrDefault()?.Text;
            if (!string.IsNullOrWhiteSpace(content)) return new(content);

            return new("");
        }

        public async Task<JsonObject> GetStructuredResponse(Conversations prompt, JsonObject responseFormat)
        {
            EnsureInitialized();

            ChatCompletionOptions options = new()
            {
                ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                    jsonSchemaFormatName: "structured_response",
                    jsonSchema: BinaryData.FromString(responseFormat.ToJsonString()),
                    jsonSchemaIsStrict: true)
            };

            ChatCompletion completion = await _chatClient!.CompleteChatAsync(prompt.ToChatMessages(), options);

            using JsonDocument structuredJson = JsonDocument.Parse(completion.Content[0].Text);
            return JsonNode.Parse(structuredJson.RootElement.GetRawText())!.AsObject();
        }

    }
}

