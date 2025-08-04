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

        public Task<Tool> GetToolCallResponse(ChatHistory prompt, ITools tools)
            => ProcessToolCall(prompt, tools);

        public Task<Tool> GetToolCallResponse(ChatHistory prompt, params Tool[] tools)
            => ProcessToolCall(prompt, new Tools(tools));

        private async Task<Tool> ProcessToolCall(ChatHistory prompt, ITools tools, bool forceToolCall = false, int maxRetries = 30)
        {
            var intPrompt = ChatHistory.Clone(prompt);
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


            string lastContent = null;

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                var response = await _chatClient.CompleteChatAsync(ToChatMessages(intPrompt), options);
                var result = response.Value;

                var toolCall = result.ToolCalls?.FirstOrDefault();
                string? content = result.Content?.FirstOrDefault()?.Text;

                if (!string.IsNullOrWhiteSpace(content) && attempt > 0 && string.Equals(content, lastContent, StringComparison.Ordinal))
                {
                    return new Tool { AssistantMessage = content };
                }
                lastContent = content;

                if (toolCall != null && tools!.Contains(toolCall.FunctionName))
                {
                    var registered = tools.Get(toolCall.FunctionName);
                    var input = toolCall.FunctionArguments.ToObjectFromJson<JsonObject>();

                    if (registered?.Parameters is JsonObject schema && input != null)
                    {
                        var inputKeys = input.Select(p => p.Key).ToHashSet();
                        var schemaKeys = schema["properties"]?.AsObject()?.Select(p => p.Key).ToHashSet() ?? new();

                        if (schemaKeys.SetEquals(inputKeys))
                        {
                            return new Tool
                            {
                                Id = toolCall.Id ?? Guid.NewGuid().ToString(),
                                Name = toolCall.FunctionName,
                                Parameters = input,
                                AssistantMessage = content
                            };
                        }
                        else if (attempt < maxRetries)
                        {
                            intPrompt.Add(Role.System, $"Invalid parameters for `{toolCall.FunctionName}`. Try again with valid parameters {string.Join(", ", schemaKeys)}");
                            continue;
                        }
                    }
                    else if (attempt < maxRetries)
                    {
                        intPrompt.Add(Role.System, $"The function `{toolCall.FunctionName}` is not available. Available tools: {string.Join(", ", tools!.Select(t => t.Name))}.");
                        continue;
                    }
                }
                else if (string.IsNullOrWhiteSpace(content) && (attempt < maxRetries))
                {
                    intPrompt.Add(Role.System,
                        $"The last response was empty or invalid. Please return a valid tool call using one of: {string.Join(", ", tools.Select(t => t.Name))}.");
                    continue;
                }
                else if (!string.IsNullOrWhiteSpace(content))
                {
                    var pattern = @"(?<json>\{[^{}]*(?:""name""|""arguments"")[\s\S]*?\})";
                    var match = Regex.Match(content, pattern, RegexOptions.Singleline);
                    if (match.Success)
                    {
                        try
                        {
                            var jsonStr = match.Groups["json"].Value.Trim();
                            if (string.IsNullOrWhiteSpace(jsonStr) || jsonStr == "{}")
                            {
                                if (attempt < maxRetries)
                                {
                                    intPrompt.Add(Role.System, $"Empty tool call received. Please provide a valid tool call with name and arguments.");
                                    continue;
                                }
                            }
                            var json = JsonRepair.RepairAndParse(jsonStr);
                            if (json != null)
                            {
                                string name = json["name"]?.ToString() ?? "";
                                JsonObject? arguments = json["arguments"]?.AsObject();
                                string? message = json["message"]?.ToString();

                                if (!string.IsNullOrWhiteSpace(name) && arguments != null)
                                {
                                    if (tools.Contains(name))
                                    {
                                        var registered = tools.Get(name);
                                        var schema = registered?.Parameters?.AsObject();

                                        if (schema != null)
                                        {
                                            var inputKeys = arguments.Select(p => p.Key).ToHashSet();
                                            var schemaKeys = schema["properties"]?.AsObject()?.Select(p => p.Key).ToHashSet() ?? new();

                                            if (schemaKeys.SetEquals(inputKeys))
                                            {
                                                return new Tool
                                                {
                                                    Id = Guid.NewGuid().ToString(),
                                                    Name = name,
                                                    Parameters = arguments,
                                                    AssistantMessage = message
                                                };
                                            }
                                            else if (attempt < maxRetries)
                                            {
                                                intPrompt.Add(Role.System, $"Invalid parameters for `{arguments}`. Try again with valid parameters {string.Join(", ", schemaKeys)}");
                                                continue;
                                            }
                                        }
                                    }
                                    else if (attempt < maxRetries)
                                    {
                                        intPrompt.Add(Role.System, $"The function `{name}` is not available. Available tools: {string.Join(", ", tools!.Select(t => t.Name))}.");
                                        continue;
                                    }
                                }
                                else if (string.IsNullOrWhiteSpace(message) && (attempt < maxRetries))
                                {
                                    intPrompt.Add(Role.System,
                                        $"The last response was empty or invalid. Please return a valid tool call using one of: {string.Join(", ", tools.Select(t => t.Name))}.");
                                    continue;
                                }
                                else if (!string.IsNullOrWhiteSpace(message))
                                {
                                    return new Tool
                                    {
                                        AssistantMessage = message
                                    };
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to parse structured response - {ex}");
                        }
                    }
                    else
                    {
                        return new Tool
                        {
                            AssistantMessage = content
                        };
                    }
                }
            }
            return new Tool { AssistantMessage = "Failed to generate a valid tool call/response after retry." };
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

