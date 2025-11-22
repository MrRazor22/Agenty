using Agenty.LLMCore.ChatHandling;
using Agenty.LLMCore.JsonSchema;
using Agenty.LLMCore.Messages;
using Agenty.LLMCore.Runtime;
using Agenty.LLMCore.ToolHandling;
using OpenAI;
using OpenAI.Chat;
using SharpToken;
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

        public static void ApplySamplingOptions(
    this ChatCompletionOptions opts,
    LLMRequestBase? options)
        {
            // No sampling → still apply reasoning mode
            if (options?.Sampling == null)
            {
                ApplyReasoningMode(opts, options);
                return;
            }

            var s = options.Sampling;

            // --- Temperature ---
            if (s.Temperature != null)
                opts.Temperature = s.Temperature;

            // --- TopP ---
            if (s.TopP != null)
                opts.TopP = s.TopP;

            // --- Max tokens ---
            if (s.MaxOutputTokens != null)
                opts.MaxOutputTokenCount = s.MaxOutputTokens;

            // --- Seed (deterministic output) ---
            // Suppress diagnostic OPENAI001: 'OpenAI.Chat.ChatCompletionOptions.Seed' is for evaluation purposes only and is subject to change or removal in future updates.
#pragma warning disable OPENAI001
            if (s.Seed != null)
                opts.Seed = s.Seed;
#pragma warning restore OPENAI001

            // --- Stop sequences ---
            if (s.StopSequences != null && s.StopSequences.Count > 0)
            {
                foreach (var stop in s.StopSequences)
                    opts.StopSequences.Add(stop);
            }

            // Also allow reasoning mode to override if needed
            ApplyReasoningMode(opts, options);
        }

        private static void ApplyReasoningMode(ChatCompletionOptions opts, LLMRequestBase options)
        {
            switch (options.Reasoning)
            {
                case ReasoningMode.Deterministic:
                    opts.Temperature = 0f;
                    opts.TopP = 1f;
                    break;
                case ReasoningMode.Planning:
                    opts.Temperature = 0.3f;
                    opts.TopP = 1f;
                    break;
                case ReasoningMode.Balanced:
                    opts.Temperature = 0.9f;
                    opts.TopP = 0.5f;
                    break;
                case ReasoningMode.Creative:
                    opts.Temperature = 0.9f;
                    opts.TopP = 1f;
                    break;
                default:
                    opts.Temperature = 0.5f;
                    opts.TopP = 1f;
                    break;
            }
        }
    }
}
