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
        public async Task<Tool> ProcessToolCall(ChatHistory prompt, ITools tools, bool forceToolCall = false, int maxRetries = 30)
        {
            if (tools == null || !tools.Any())
                throw new ArgumentNullException(nameof(tools), "No tools provided for function call response.");

            var intPrompt = ChatHistory.Clone(prompt);

            var chatTools = tools
                .Select(tool => ChatTool.CreateFunctionTool(
                    tool.Name,
                    tool.Description,
                    BinaryData.FromString(tool.Arguments.ToJsonString())))
                .ToList();

            ChatCompletionOptions options = new()
            {
                ToolChoice = forceToolCall
                    ? ChatToolChoice.CreateRequiredChoice()
                    : ChatToolChoice.CreateAutoChoice()
            };

            chatTools.ForEach(t => options.Tools.Add(t));

            string? lastContent = null;

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                var response = await _chatClient.CompleteChatAsync(ToChatMessages(intPrompt), options);
                var result = response.Value;

                var toolCall = TryExtractStructuredToolCall(result, tools);
                if (toolCall != null)
                    return toolCall;

                string? content = result.Content?.FirstOrDefault()?.Text;
                if (!string.IsNullOrWhiteSpace(content))
                {
                    if (attempt > 0 && string.Equals(content, lastContent, StringComparison.Ordinal))
                        return new Tool { AssistantMessage = content };

                    lastContent = content;

                    var tool = TryExtractInlineToolCall(content, tools);
                    if (tool != null)
                        return tool;

                    // If content is not a tool call, return it as a message
                    return new Tool { AssistantMessage = content };
                }

                // Retry: Inject feedback to help model correct itself
                if (attempt < maxRetries)
                {
                    intPrompt.Add(Role.System, BuildRetryMessage(result, tools));
                }
            }

            return new Tool { AssistantMessage = "Failed to generate a valid tool call/response after retry." };
        }

        private Tool? TryExtractStructuredToolCall(ChatCompletion result, ITools tools)
        {
            var toolCall = result?.ToolCalls?.FirstOrDefault();
            if (toolCall == null || !tools.Contains(toolCall.FunctionName))
                return null;

            var registered = tools.Get(toolCall.FunctionName);
            var input = toolCall.FunctionArguments.ToObjectFromJson<JsonObject>();

            if (registered?.Arguments is JsonObject schema && input != null)
            {
                if (IsValidToolArguments(input, schema))
                {
                    return new Tool
                    {
                        Id = toolCall.Id ?? Guid.NewGuid().ToString(),
                        Name = toolCall.FunctionName,
                        Arguments = input,
                        AssistantMessage = result?.Content?.FirstOrDefault()?.Text
                    };
                }
            }

            return null;
        }

        // Match any tag that includes "tool" and contains a well-formed JSON block
        static readonly string tagPattern = @"(?i)
            [\[\{\(<]         # opening bracket of any type
            [^\]\}\)>]*?      # non-greedy anything except closing brackets
            \b\w*tool\w*\b    # word “tool” inside (word boundary optional if you want partials)
            [^\]\}\)>]*?      # again anything before closing
            [\]\}\)>]         # closing bracket
        ";

        static readonly string toolTagPattern = @$"(?ix)
            (?<open>{tagPattern})         # opening tag like [TOOL_REQUEST]
            \s*                           # optional whitespace/newlines
            (?<json>\{{[\s\S]*?\}})         # JSON object
            \s*                           # optional whitespace/newlines
            (?<close>{tagPattern})        # closing tag like [END_TOOL_REQUEST]
        ";

        static readonly string looseToolJsonPattern = @"
                    (?<json>
                        \{
                          \s*""name""\s*:\s*""[^""]+""
                          \s*,\s*
                          ""arguments""\s*:\s*\{[\s\S]*\}
                          \s*
                        \}
                    )
                ";

        private Tool? TryExtractInlineToolCall(string content, ITools tools)
        {
            var opts = RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace;

            // 2. Find the very first tagged or loose‐JSON match
            var match = Regex.Matches(content, toolTagPattern, opts)
                             .Cast<Match>()
                             .FirstOrDefault()
                     ?? Regex.Matches(content, looseToolJsonPattern, opts)
                             .Cast<Match>()
                             .FirstOrDefault();

            // 3. If nothing matched at all, bail
            if (match == null)
                return null;

            // 4. Extract the JSON and try to parse it
            var jsonStr = match.Groups["json"].Value.Trim();
            JsonObject? node = null;
            try
            {
                node = JsonNode.Parse(jsonStr)?.AsObject();
            }
            catch { /* invalid JSON */ }

            // 5. If it *is* a valid call (has name+arguments, registered, valid args schema) → return full Tool
            if (node != null &&
                node.ContainsKey("name") &&
                node.ContainsKey("arguments") &&
                tools.Contains(node["name"]?.ToString()))
            {
                var name = node["name"]!.ToString();
                var args = node["arguments"] as JsonObject;

                var reg = tools.Get(name);
                var schema = reg?.Arguments?.AsObject();

                if (schema != null && IsValidToolArguments(args, schema))
                {
                    // strip out JUST the JSON (and any tags) from the original content
                    var cleaned = content.Substring(0, match.Index).Trim();

                    return new Tool
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = name,
                        Arguments = args,
                        AssistantMessage = cleaned
                    };
                }
            }

            // 6. Otherwise: it's an *invalid* or unregistered call → strip it and return only preceding text
            var before = content.Substring(0, match.Index).Trim();
            return new Tool
            {
                AssistantMessage = before
            };

        }

        private bool IsValidToolArguments(JsonObject input, JsonObject schema)
        {
            var inputKeys = input.Select(p => p.Key).ToHashSet();
            var schemaKeys = schema["properties"]?.AsObject()?.Select(p => p.Key).ToHashSet() ?? new();
            return schemaKeys.SetEquals(inputKeys);
        }
        private string BuildRetryMessage(ChatCompletion result, ITools tools)
        {
            var availableTools = string.Join(", ", tools.Select(t => t.Name));
            return $"The last response was empty or invalid. Please return a valid tool call using one of: {availableTools}.";
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

