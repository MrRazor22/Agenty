using OpenAI;
using OpenAI.Chat;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Agenty.LLMCore.Providers.OpenAI
{
    public static class OpenAIExtensions
    {
        public static ChatToolChoice ToChatToolChoice(this ToolCallMode mode)
        {
            return mode switch
            {
                ToolCallMode.None => ChatToolChoice.CreateNoneChoice(),
                ToolCallMode.Required => ChatToolChoice.CreateRequiredChoice(),
                _ => ChatToolChoice.CreateAutoChoice()
            };
        }
        // Converts your Tool collection to OpenAI ChatTools (functions)
        public static List<ChatTool> ToChatTools(this IEnumerable<Tool> tools)
        {
            return tools.Select(tool =>
                ChatTool.CreateFunctionTool(
                    tool.Name,
                    tool.Description ?? "",
                    BinaryData.FromString(tool.ParametersSchema?.ToJsonString() ?? "{\"type\":\"object\"}"))
            ).ToList();
        }

        // Converts ChatHistory to IEnumerable<ChatMessage> suitable for OpenAI chat completion
        public static IEnumerable<ChatMessage> ToChatMessages(this Conversation history)
        {
            var messages = history.ToList();

            for (int i = 0; i < messages.Count; i++)
            {
                var msg = messages[i];
                bool isLast = i == messages.Count - 1;

                yield return msg.Role switch
                {
                    Role.System => ChatMessage.CreateSystemMessage(msg.Content ?? ""),
                    Role.User => ChatMessage.CreateUserMessage(msg.Content ?? ""),
                    Role.Assistant when msg.toolCallInfo != null && !string.IsNullOrEmpty(msg.toolCallInfo.Name) =>
                        ChatMessage.CreateAssistantMessage(
                            toolCalls: new[]
                            {
                                ChatToolCall.CreateFunctionToolCall(
                                    id: msg.toolCallInfo.Id,
                                    functionName: msg.toolCallInfo.Name,
                                    functionArguments: BinaryData.FromObjectAsJson(msg.toolCallInfo.Arguments ?? new JsonObject()))
                            }),
                    Role.Assistant =>
                        string.IsNullOrWhiteSpace(msg.Content)
                            ? isLast
                                ? throw new InvalidOperationException("Assistant message content cannot be null or empty.")
                                : ChatMessage.CreateAssistantMessage(string.Empty)
                            : ChatMessage.CreateAssistantMessage(msg.Content),
                    Role.Tool when msg.toolCallInfo != null =>
                        ChatMessage.CreateToolMessage(msg.toolCallInfo.Id, msg.Content ?? ""),
                    Role.Tool =>
                        isLast
                            ? throw new InvalidOperationException("ToolCallInfo required for tool message.")
                            : ChatMessage.CreateToolMessage("unknown", msg.Content ?? ""),
                    _ => throw new InvalidOperationException($"Unknown message role {msg.Role}")
                };
            }
        }

        public static JsonObject ToOpenAiSchema(this Tool tool)
        {
            // Ensure parameters schema has "type": "object"
            if (!tool.ParametersSchema.ContainsKey("type"))
                tool.ParametersSchema["type"] = "object";

            return new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["parameters"] = tool.ParametersSchema?.DeepClone()
            };
        }

        public static string ToOpenAiSchemaJson(this Tool tool, bool indented = true)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = indented
            };

            return JsonSerializer.Serialize(tool.ToOpenAiSchema(), options);
        }
    }
}
