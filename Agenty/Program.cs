using Agenty.AgentCore;
using Agenty.LLMCore;
using Agenty.LLMCore.BuiltInTools;
using Agenty.LLMCore.OpenAI;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
using System;
using System.ComponentModel;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ILogger = Agenty.LLMCore.ILogger;

namespace Agenty
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            ILogger logger = new LLMCore.ConsoleLogger();

            var llm = new OpenAILLMClient();
            llm.Initialize("http://127.0.0.1:1234/v1", "lmstudio", "any_model");

            ToolCoordinator toolCordinator = new ToolCoordinator(llm);

            ITools tools = new Tools();
            tools.RegisterAll<GeoTools>(); // auto-registers static methods in UserTools

            var chatHistory = new ChatHistory();
            chatHistory.OnChat += (chat) =>
            {
                var level = (chat.Role == Role.Assistant || chat.Role == Role.User || chat.Role == Role.Tool)
                    ? LogLevel.Information : LogLevel.Debug;

                var content = !string.IsNullOrWhiteSpace(chat.Content) ? chat.Content : chat.toolCallInfo?.ToString();
                if (string.IsNullOrWhiteSpace(content)) content = "<empty>";

                logger.Log(level, nameof(ChatHistory), $"{chat.Role}: '{content}'");
            };

            chatHistory.Add(Role.System, "You are an assistant." +
                 "Plan and answer one by one" +
                 "if not sure on what to respind, express that to user directly" +
                 "Use relevant tools if needed, or respond directly." +
                 "Provide your answers short and sweet");


            Console.WriteLine("🤖 Welcome to Agenty ChatBot! Type 'exit' to quit.\n");

            while (true)
            {
                Console.Write("You: ");
                string? input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(input) || input.Equals("exit", StringComparison.OrdinalIgnoreCase))
                    break;

                chatHistory.Add(Role.User, input);

                ToolCall toolCall;
                try
                {
                    toolCall = await toolCordinator.GetStructuredToolCall(chatHistory, tools);
                }
                catch (Exception ex)
                {
                    chatHistory.Add(Role.Assistant, "Error fetching tool call.");
                    continue;
                }

                await ExecuteToolChain(toolCall, chatHistory, tools, toolCordinator);
            }

            Console.WriteLine("👋 Exiting Agenty ChatBot.");
        }

        private static async Task ExecuteToolChain(ToolCall initialCall, ChatHistory chat, ITools tools, ToolCoordinator toolCordinator)
        {
            ToolCall currentToolCall = initialCall;

            while (true)
            {
                if (!string.IsNullOrWhiteSpace(currentToolCall.AssistantMessage))
                {
                    chat.Add(Role.Assistant, currentToolCall.AssistantMessage);
                }

                if (string.IsNullOrWhiteSpace(currentToolCall.Name))
                    return;

                chat.Add(Role.Assistant, tool: currentToolCall);

                object? result;
                try
                {
                    result = await toolCordinator.Invoke<object>(currentToolCall, tools);
                }
                catch (Exception ex)
                {
                    chat.Add(Role.Assistant, $"Tool invocation failed - {ex}");
                    return;
                }

                chat.Add(Role.Tool, result?.ToString(), currentToolCall);

                try
                {
                    currentToolCall = await toolCordinator.GetStructuredToolCall(chat, tools);
                }
                catch (Exception ex)
                {
                    chat.Add(Role.Assistant, $"Error fetching next step - {ex}");
                    return;
                }

                if (string.IsNullOrWhiteSpace(currentToolCall.AssistantMessage) &&
                    string.IsNullOrWhiteSpace(currentToolCall.Name))
                    return;
            }
        }
    }
}

