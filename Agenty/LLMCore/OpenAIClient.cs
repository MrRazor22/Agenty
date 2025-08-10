using Agenty.Utilities;
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

namespace Agenty.LLMCore
{
    public class OpenAIClient() : ILLMClient
    {
        private OpenAI.OpenAIClient _client;
        private ChatClient _chatClient;

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
        public async Task<string> GetResponse(ChatHistory prompt)
        {
            var response = await _chatClient.CompleteChatAsync(ToChatMessages(prompt));

            var contentParts = response.Value.Content;
            var textContent = string.Join("", contentParts.Select(part => part.Text));
            return textContent;
        }

        public async IAsyncEnumerable<string> GetStreamingResponse(ChatHistory prompt)
        {
            AsyncCollectionResult<StreamingChatCompletionUpdate> responseUpdates
                = _chatClient.CompleteChatStreamingAsync(ToChatMessages(prompt));
            await foreach (var update in responseUpdates)
            {
                foreach (var part in update.ContentUpdate)
                {
                    yield return part.Text;
                }
            }
        }
        public async Task<ToolCall> GetToolCallResponse(ChatHistory prompt, ITools tools, bool forceToolCall = false)
        {
            List<ChatTool> chatTools = ToChatTools(tools);

            ChatCompletionOptions options = new() { ToolChoice = forceToolCall ? ChatToolChoice.CreateRequiredChoice() : ChatToolChoice.CreateAutoChoice() };

            chatTools.ForEach(t => options.Tools.Add(t));

            var response = await _chatClient.CompleteChatAsync(ToChatMessages(prompt), options);
            var result = response.Value;

            var chatToolCall = result?.ToolCalls?.FirstOrDefault();
            if (chatToolCall != null)
            {
                if (tools.Contains(chatToolCall.FunctionName))
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

        private static List<ChatTool> ToChatTools(ITools tools)
        {
            return tools.RegisteredTools
                            .Select(tool => ChatTool.CreateFunctionTool(
                                tool.Name,
                                tool.Description,
                                BinaryData.FromString(tool.SchemaDefinition.ToJsonString())))
                            .ToList();
        }

        public async Task<JsonObject> GetStructuredResponse(ChatHistory prompt, JsonObject responseFormat)
        {
            ChatCompletionOptions options = new()
            {
                ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                    jsonSchemaFormatName: "structured_response",
                    jsonSchema: BinaryData.FromString(responseFormat.ToJsonString()),
                    jsonSchemaIsStrict: true)
            };

            ChatCompletion completion = await _chatClient.CompleteChatAsync(ToChatMessages(prompt), options);

            using JsonDocument structuredJson = JsonDocument.Parse(completion.Content[0].Text);
            return JsonNode.Parse(structuredJson.RootElement.GetRawText())!.AsObject();
        }

        private IEnumerable<ChatMessage> ToChatMessages(ChatHistory prompt)
        {
            var list = prompt.ToList();
            for (int i = 0; i < list.Count; i++)
            {
                var msg = list[i];
                bool isLast = i == list.Count - 1;

                yield return msg.Role switch
                {
                    Role.System => ChatMessage.CreateSystemMessage(msg.Content),
                    Role.User => ChatMessage.CreateUserMessage(msg.Content),
                    Role.Assistant when msg.toolCallInfo != null && msg.toolCallInfo.Name != null =>
                        ChatMessage.CreateAssistantMessage(
                            toolCalls: new[]
                            {
                        ChatToolCall.CreateFunctionToolCall(
                            id: msg.toolCallInfo.Id,
                            functionName: msg.toolCallInfo.Name,
                            functionArguments: BinaryData.FromObjectAsJson(msg.toolCallInfo.Arguments))
                            }),
                    Role.Assistant => string.IsNullOrWhiteSpace(msg.Content)
                        ? (isLast
                            ? throw new InvalidOperationException("Assistant message content cannot be null or empty.")
                            : ChatMessage.CreateAssistantMessage(string.Empty))
                        : ChatMessage.CreateAssistantMessage(msg.Content),
                    Role.Tool when msg.toolCallInfo is not null =>
                        ChatMessage.CreateToolMessage(msg.toolCallInfo.Id, msg.Content),
                    Role.Tool =>
                        isLast
                            ? throw new InvalidOperationException("ToolCallInfo required for tool message.")
                            : ChatMessage.CreateToolMessage("unknown", msg.Content ?? string.Empty),
                    _ => throw new InvalidOperationException("Invalid message role.")
                };
            }
        }

    }
}

