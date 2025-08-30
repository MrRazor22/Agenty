using Agenty.LLMCore.ToolHandling;
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
            foreach (var msg in history)
            {
                switch (msg.Role)
                {
                    case Role.System:
                        yield return ChatMessage.CreateSystemMessage(msg.Content ?? "");
                        break;

                    case Role.User:
                        yield return ChatMessage.CreateUserMessage(msg.Content ?? "");
                        break;

                    case Role.Assistant when msg.ToolCalls != null && msg.ToolCalls.Count > 0:
                        yield return ChatMessage.CreateAssistantMessage(
                            toolCalls: msg.ToolCalls.Select(tc =>
                                ChatToolCall.CreateFunctionToolCall(
                                    id: tc.Id,
                                    functionName: tc.Name,
                                    functionArguments: BinaryData.FromObjectAsJson(tc.Arguments ?? new JsonObject()))
                            ).ToArray());
                        break;

                    case Role.Assistant:
                        yield return ChatMessage.CreateAssistantMessage(msg.Content ?? "");
                        break;

                    case Role.Tool when msg.ToolCalls != null && msg.ToolCalls.Count == 1:
                        var tc = msg.ToolCalls[0];
                        yield return ChatMessage.CreateToolMessage(tc.Id, msg.Content ?? "");
                        break;

                    default:
                        throw new InvalidOperationException($"Invalid message state for role {msg.Role}");
                }
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
