using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using System.Text.Json;
using System.Text.Json.Nodes;

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

        public Task<Tool> GetToolCallResponse(ChatHistory prompt, ITools tools)
            => ProcessToolCall(prompt, tools);

        public Task<Tool> GetToolCallResponse(ChatHistory prompt, params Tool[] tools)
            => ProcessToolCall(prompt, new Tools(tools));

        public async Task<Tool> ProcessToolCall(ChatHistory prompt, ITools tools, bool forceToolCall = false)
        {
            if (tools == null || !tools.Any())
                throw new ArgumentNullException(nameof(tools), "No tools provided for function call response.");

            List<ChatTool> chatTools = tools!
                .Select(tool => ChatTool.CreateFunctionTool(
                    tool.Name,
                    tool.Description,
                    BinaryData.FromString(tool.Parameters.ToJsonString())))
                .ToList();

            ChatCompletionOptions options = new();
            chatTools.ForEach(t => options.Tools.Add(t));
            if (forceToolCall) options.ToolChoice = ChatToolChoice.CreateRequiredChoice();

            var response = await _chatClient.CompleteChatAsync(ToChatMessages(prompt), options);
            var result = response.Value;
            var assistantResponse = result.Content.FirstOrDefault()?.Text;

            var firstToolCall = result.ToolCalls.FirstOrDefault();
            if (firstToolCall == null)
                return new Tool { AssistantMessage = assistantResponse };

            return new Tool
            {
                Id = firstToolCall.Id,
                Name = firstToolCall.FunctionName,
                Parameters = firstToolCall.FunctionArguments.ToObjectFromJson<JsonObject>() ?? new JsonObject(),
                AssistantMessage = assistantResponse
            };
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
            foreach (var msg in prompt)
            {
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
                                    functionArguments: BinaryData.FromObjectAsJson(msg.toolCallInfo.Parameters))
                            }),
                    Role.Assistant => ChatMessage.CreateAssistantMessage(msg.Content),
                    Role.Tool when msg.toolCallInfo is not null =>
                        ChatMessage.CreateToolMessage(msg.toolCallInfo.Id, msg.Content),
                    Role.Tool =>
                        throw new InvalidOperationException("ToolCallId required for tool message."),
                    _ => throw new InvalidOperationException("Invalid message role.")
                };
            }
        }
    }
}

