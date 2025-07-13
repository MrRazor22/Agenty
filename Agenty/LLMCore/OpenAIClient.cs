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
        public async Task<string> GetResponse(IPrompt prompt)
        {
            var response = await _chatClient.CompleteChatAsync(ToChatMessages(prompt));

            var contentParts = response.Value.Content;
            var textContent = string.Join("", contentParts.Select(part => part.Text));
            return textContent;
        }

        public async IAsyncEnumerable<string> GetStreamingResponse(IPrompt prompt)
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

        public async Task<ToolCallResponse> GetFunctionCallResponse(IPrompt prompt, List<Tool> tools)
        {
            if (tools == null || tools.Count == 0)
                new ArgumentNullException("No Tools provided fro function call respinse");

            List<ChatTool> chatTools = tools!
                .Select(tool => ChatTool.CreateFunctionTool(
                    tool.Name,
                    tool.Description,
                    BinaryData.FromString(tool.ParameterSchema.ToJsonString())))
                .ToList();

            ChatCompletionOptions options = new();
            chatTools.ForEach(t => options.Tools.Add(t));
            //options.ToolChoice = ChatToolChoice.CreateRequiredChoice();

            var response = await _chatClient.CompleteChatAsync(ToChatMessages(prompt), options);
            var result = response.Value;
            var assistantResponse = result.Content.FirstOrDefault()?.Text;

            var toolCalls = result.ToolCalls.Select(call => new ToolCallInfo
            {
                Id = call.Id,
                Name = call.FunctionName,
                Parameters = call.FunctionArguments.ToObjectFromJson<JsonObject>()
            }).ToList();

            return new ToolCallResponse
            {
                AssistantMessage = toolCalls.Count == 0 ? assistantResponse : null,
                ToolCalls = toolCalls
            };
        }

        public async Task<JsonObject> GetStructuredResponse(IPrompt prompt, JsonObject responseFormat)
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

        private IEnumerable<ChatMessage> ToChatMessages(IPrompt prompt)
        {
            foreach (var msg in prompt.Messages)
            {
                yield return msg.Role switch
                {
                    ChatRole.System => ChatMessage.CreateSystemMessage(msg.Content),
                    ChatRole.User => ChatMessage.CreateUserMessage(msg.Content),
                    ChatRole.Assistant when msg.toolCallInfo != null && msg.toolCallInfo.Name != null =>
                        ChatMessage.CreateAssistantMessage(
                            toolCalls: new[]
                            {
                                ChatToolCall.CreateFunctionToolCall(
                                    id: msg.toolCallInfo.Id,
                                    functionName: msg.toolCallInfo.Name,
                                    functionArguments: BinaryData.FromObjectAsJson(msg.toolCallInfo.Parameters))
                            }),
                    ChatRole.Assistant => ChatMessage.CreateAssistantMessage(msg.Content),
                    ChatRole.Tool when msg.toolCallInfo is not null =>
                        ChatMessage.CreateToolMessage(msg.toolCallInfo.Id, msg.Content),
                    ChatRole.Tool =>
                        throw new InvalidOperationException("ToolCallId required for tool message."),
                    _ => throw new InvalidOperationException("Invalid message role.")
                };
            }
        }
    }
}

