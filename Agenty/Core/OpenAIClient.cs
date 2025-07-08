using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Agenty.Core
{
    public class OpenAIClient(ToolRegistry toolRegistry) : ILLMClient
    {
        private OpenAI.OpenAIClient _client;
        private ChatClient _chatClient;
        private List<ChatMessage> _messages = new();
        private string systemPrompt = "You are an helpfull assistant";

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
        public async Task<string> GenerateResponse(IPrompt prompt)
        {
            var response = await _chatClient.CompleteChatAsync(ToChatMessages(prompt));

            var contentParts = response.Value.Content;
            var textContent = string.Join("", contentParts.Select(part => part.Text));
            return textContent;
        }

        public async IAsyncEnumerable<string> GenerateStreamingResponse(IPrompt prompt)
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

        public async Task<List<ToolCallInfo>> GetFunctionCallResponse(IPrompt prompt)
        {
            var allTools = toolRegistry.GetRegisteredTools();
            return await GetFunctionCallResponse(prompt, allTools);
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
                options.Tools.Add(tool);

            var response = await _chatClient.CompleteChatAsync(ToChatMessages(prompt), options);

            var result = response.Value;
            var assistantResponse = result.Content.FirstOrDefault()?.Text;

            var toolCalls = new List<ToolCallInfo>();

            if (result.ToolCalls.Count > 0)
            {
                foreach (var toolCall in result.ToolCalls)
                {
                    var name = toolCall.FunctionName;
                    var argsJson = toolCall.FunctionArguments.ToObjectFromJson<JsonObject>();

                    toolCalls.Add(new ToolCallInfo
                    {
                        Id = toolCall.Id,
                        AssistantMessage = assistantResponse,
                        Name = toolCall.FunctionName,
                        Parameters = toolCall.FunctionArguments.ToObjectFromJson<JsonObject>()
                    });
                }
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
                    ChatRole.User => ChatMessage.CreateUserMessage(msg.Content),
                    ChatRole.Assistant => ChatMessage.CreateAssistantMessage(msg.Content),
                    ChatRole.Tool when msg.ToolId is not null =>
                        ChatMessage.CreateToolMessage(msg.ToolId, msg.Content),
                    ChatRole.Tool =>
                        throw new InvalidOperationException("ToolCallId required for tool message."),
                    _ => throw new InvalidOperationException("Invalid message role.")
                };
            }
        }
    }
}

