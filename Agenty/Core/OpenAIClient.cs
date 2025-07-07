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
        public async Task<string> GenerateResponse(string prompt)
        {
            var response = await _chatClient.CompleteChatAsync(new[]
            {
            ChatMessage.CreateUserMessage(prompt)
        });

            var contentParts = response.Value.Content;
            var textContent = string.Join("", contentParts.Select(part => part.Text));
            return textContent;
        }

        public async IAsyncEnumerable<string> GenerateStreamingResponse(string prompt)
        {
            AsyncCollectionResult<StreamingChatCompletionUpdate> responseUpdates = _chatClient.CompleteChatStreamingAsync(prompt);
            await foreach (var update in responseUpdates)
            {
                foreach (var part in update.ContentUpdate)
                {
                    yield return part.Text;
                }
            }
        }

        public async Task<List<ToolCallInfo>> GetFunctionCallResponse(string prompt)
        {
            var allTools = toolRegistry.GetRegisteredTools();
            return await GetFunctionCallResponse(prompt, allTools);
        }

        public async Task<List<ToolCallInfo>> GetFunctionCallResponse(string prompt, List<Tool> tools)
        {
            if (tools == null || tools.Count == 0)
                new ArgumentNullException("No Tools provided fro function call respinse");

            _messages.Add(new UserChatMessage(prompt));

            List<ChatTool> chatTools = tools!
                .Select(tool => ChatTool.CreateFunctionTool(
                    tool.Name,
                    tool.Description,
                    BinaryData.FromString(tool.ParameterSchema.ToJsonString())))
                .ToList();

            ChatCompletionOptions options = new();
            foreach (var tool in chatTools)
                options.Tools.Add(tool);

            var response = await _chatClient.CompleteChatAsync(_messages, options);
            _messages.Add(new AssistantChatMessage(response));

            var result = response.Value;

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
                        Name = toolCall.FunctionName,
                        Parameters = toolCall.FunctionArguments.ToObjectFromJson<JsonObject>()
                    });
                }
            }

            return toolCalls;
        }

        public void SetSystemPrompt(string prompt)
        {
            _messages.Add(new SystemChatMessage(systemPrompt));
        }

        public JsonObject GetStructuredResponse(string prompt, JsonObject responseFormat)
        {
            _messages.Add(new UserChatMessage(prompt));

            ChatCompletionOptions options = new()
            {
                ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                    jsonSchemaFormatName: "structured_response",
                    jsonSchema: BinaryData.FromString(responseFormat.ToJsonString()),
                    jsonSchemaIsStrict: true)
            };

            ChatCompletion completion = _chatClient.CompleteChat(_messages, options);

            using JsonDocument structuredJson = JsonDocument.Parse(completion.Content[0].Text);
            return JsonNode.Parse(structuredJson.RootElement.GetRawText())!.AsObject();
        }
    }
}

