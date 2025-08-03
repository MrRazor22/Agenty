using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using System.Linq;
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

        private async Task<Tool> ProcessToolCall(ChatHistory prompt, ITools tools, bool forceToolCall = false, int maxRetries = 3)
        {
            if (tools == null || !tools.Any())
                throw new ArgumentNullException(nameof(tools), "No tools provided for function call response.");

            List<ChatTool> chatTools = tools!
                .Select(tool => ChatTool.CreateFunctionTool(
                    tool.Name,
                    tool.Description,
                    BinaryData.FromString(tool.Parameters.ToJsonString())))
                .ToList();

            ChatCompletionOptions options = new() { ToolChoice = forceToolCall ? ChatToolChoice.CreateRequiredChoice() : ChatToolChoice.CreateAutoChoice() };
            chatTools.ForEach(t => options.Tools.Add(t));

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                var response = await _chatClient.CompleteChatAsync(ToChatMessages(prompt), options);
                var result = response.Value;

                var toolCall = result.ToolCalls?.FirstOrDefault();
                string? content = result.Content?.FirstOrDefault()?.Text;

                if (toolCall != null)
                {
                    if (tools.Any(t => t.Name == toolCall.FunctionName))
                    {
                        return new Tool
                        {
                            Id = toolCall.Id ?? Guid.NewGuid().ToString(),
                            Name = toolCall.FunctionName,
                            Parameters = toolCall.FunctionArguments.ToObjectFromJson<JsonObject>() ?? new JsonObject(),
                        };
                    }
                    else if (attempt < maxRetries)
                    {
                        prompt.Add(Role.Assistant, $"The function `{toolCall.FunctionName}` is not available. Please choose a valid function.");
                        continue;
                    }
                }
                else if (string.IsNullOrWhiteSpace(content) && attempt < maxRetries)
                {
                    prompt.Add(Role.Assistant, "Please respond with a valid tool call or a message.");
                    continue;
                }
                else
                {
                    try
                    {
                        if (content!.Contains("\"name\"") && content.Contains("\"arguments\""))     // crude check  // helps reduce hallucinated calls
                        {
                            for (int structuredAttempt = 0; structuredAttempt <= maxRetries; structuredAttempt++)
                            {
                                var structured = await GetStructuredResponse(new ChatHistory().Add(Role.Assistant, content), tools.GetToolsSchema());
                                string name = structured["name"]?.ToString() ?? "";
                                JsonObject? arguments = structured["arguments"]?.AsObject();
                                string? message = structured["message"]?.ToString();

                                // If tool name and args present, return as tool
                                if (!string.IsNullOrWhiteSpace(name) && arguments != null)
                                {
                                    if (tools.Any(t => t.Name == name))
                                    {
                                        return new Tool
                                        {
                                            Id = Guid.NewGuid().ToString(),
                                            Name = name,
                                            Parameters = arguments,
                                            AssistantMessage = message
                                        };
                                    }
                                    else if (structuredAttempt < maxRetries)
                                    {
                                        prompt.Add(Role.Assistant, $"The function `{toolCall.FunctionName}` is not available. Please choose a valid function or return message");
                                        continue;
                                    }
                                }

                                // If no tool, but assistant gave a message
                                if (!string.IsNullOrWhiteSpace(message) && string.IsNullOrWhiteSpace(name) && arguments == null)
                                {
                                    return new Tool
                                    {
                                        AssistantMessage = message
                                    };
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to parse structured response - {ex}"); ;
                    }
                }

                return new Tool { AssistantMessage = content };
            }

            return new Tool { AssistantMessage = "Failed to generate a valid tool call after retry." };
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
                            functionArguments: BinaryData.FromObjectAsJson(msg.toolCallInfo.Parameters))
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

