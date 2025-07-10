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

        public async Task<List<ToolCallInfo>> GetFunctionCallResponse(IPrompt prompt, List<Tool> tools)
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
            foreach (var tool in chatTools)
            {
                options.Tools.Add(tool);
            }
            //options.ToolChoice = ChatToolChoice.CreateRequiredChoice();

            var response = await _chatClient.CompleteChatAsync(ToChatMessages(prompt), options);

            var result = response.Value;
            var assistantResponse = result.Content.FirstOrDefault()?.Text;

            var toolCalls = new List<ToolCallInfo>();

            if (result.ToolCalls.Count > 0)
            {
                foreach (var toolCall in result.ToolCalls)
                {
                    toolCalls.Add(new ToolCallInfo
                    {
                        Id = toolCall.Id,
                        AssistantMessage = assistantResponse,
                        Name = toolCall.FunctionName,
                        Parameters = toolCall.FunctionArguments.ToObjectFromJson<JsonObject>()
                    });
                }
            }
            else
            {
                toolCalls.Add(new ToolCallInfo
                {
                    AssistantMessage = assistantResponse
                });
            }

            return toolCalls;
        }

        public JsonObject GetStructuredResponse(IPrompt prompt, JsonObject responseFormat)
        {
            ChatCompletionOptions options = new()
            {
                ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                    jsonSchemaFormatName: "structured_response",
                    jsonSchema: BinaryData.FromString(responseFormat.ToJsonString()),
                    jsonSchemaIsStrict: true)
            };

            ChatCompletion completion = _chatClient.CompleteChat(ToChatMessages(prompt), options);

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
                    ChatRole.Assistant when msg.toolCallInfo != null && msg.toolCallInfo.AssistantMessage != null =>
                        ChatMessage.CreateAssistantMessage(msg.toolCallInfo.AssistantMessage ?? ""),
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

