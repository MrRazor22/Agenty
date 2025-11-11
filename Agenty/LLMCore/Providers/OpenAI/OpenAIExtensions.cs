using Agenty.LLMCore.ChatHandling;
using Agenty.LLMCore.JsonSchema;
using Agenty.LLMCore.Messages;
using Agenty.LLMCore.ToolHandling;
using OpenAI;
using OpenAI.Chat;
using System;
using System.Collections.Generic;
using System.Linq;

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
                    BinaryData.FromString(
                        tool.ParametersSchema?.ToString(Newtonsoft.Json.Formatting.None)
                        ?? "{\"type\":\"object\"}"
                    )
                )
            ).ToList();
        }


        // Converts ChatHistory to IEnumerable<ChatMessage> suitable for OpenAI chat completion
        public static IEnumerable<ChatMessage> ToChatMessages(this Conversation history)
        {
            foreach (var msg in history)
            {
                switch (msg.Role)
                {
                    case Role.System when msg.Content is TextContent sysText:
                        yield return ChatMessage.CreateSystemMessage(sysText.Text);
                        break;

                    case Role.User when msg.Content is TextContent userText:
                        yield return ChatMessage.CreateUserMessage(userText.Text);
                        break;

                    case Role.Assistant when msg.Content is TextContent assistantText:
                        yield return ChatMessage.CreateAssistantMessage(assistantText.Text);
                        break;

                    case Role.Assistant when msg.Content is ToolCall call:
                        yield return ChatMessage.CreateAssistantMessage(
                            toolCalls: new[]
                            {
                        ChatToolCall.CreateFunctionToolCall(
                            id: call.Id,
                            functionName: call.Name,
                            functionArguments: BinaryData.FromString(
                                call.Arguments?.ToString() ?? "{}"))
                            });
                        break;

                    case Role.Tool when msg.Content is ToolCallResult result:
                        var payload = result.Result == null
                            ? "{}"
                            : result.Result.AsJsonString(); // keep real output if exists

                        yield return ChatMessage.CreateToolMessage(
                            result.Call.Id,
                            payload);
                        break;

                    default:
                        throw new InvalidOperationException(
                            $"Invalid message state for role {msg.Role} with content {msg.Content?.GetType().Name}");
                }
            }
        }

        public static void ApplyLLMMode(this ChatCompletionOptions options, ReasoningMode mode)
        {
            switch (mode)
            {
                case ReasoningMode.Deterministic:
                    options.Temperature = 0f;
                    options.TopP = 1f;
                    break;
                case ReasoningMode.Planning:
                    options.Temperature = 0.3f;
                    options.TopP = 1f;
                    break;
                case ReasoningMode.Balanced:
                    options.Temperature = 0.5f;
                    options.TopP = 0.9f;
                    break;
                case ReasoningMode.Creative:
                    options.Temperature = 0.9f;
                    options.TopP = 1f;
                    break;
                default:
                    options.Temperature = 0.5f;
                    options.TopP = 1f;
                    break;
            }
        }
    }
}
