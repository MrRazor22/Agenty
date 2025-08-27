
using Agenty.LLMCore;
using Agenty.LLMCore.Providers.OpenAI;
using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;

namespace Agenty.AgentCore
{
    public sealed class ReActToolCallingAgent : IAgent
    {
        private ILLMClient _llm = null!;
        private ToolCoordinator _coord = null!;
        private readonly IToolRegistry _tools = new ToolRegistry();

        public static ReActToolCallingAgent Create() => new();
        private ReActToolCallingAgent() { }

        public ReActToolCallingAgent WithLLM(string baseUrl, string apiKey, string model = "any_model")
        {
            _llm = new OpenAILLMClient();
            _llm.Initialize(baseUrl, apiKey, model);
            _coord = new ToolCoordinator(_llm, _tools);
            return this;
        }

        public ReActToolCallingAgent WithTools<T>() { _tools.RegisterAll<T>(); return this; }
        public ReActToolCallingAgent WithTools(params Delegate[] fns) { _tools.Register(fns); return this; }
        public void Log(LogLevel level, string source, string message, ConsoleColor? colorOverride = null)
        {
            if (level < LogLevel.Information) return;

            var originalColor = Console.ForegroundColor;
            Console.ForegroundColor = colorOverride ?? GetColor(level);
            Console.WriteLine($"[{level}] [{source}] {message}");
            Console.ForegroundColor = originalColor;
        }


        private ConsoleColor GetColor(LogLevel level)
        {
            return level switch
            {
                LogLevel.Trace => ConsoleColor.Gray,
                LogLevel.Debug => ConsoleColor.Cyan,
                LogLevel.Information => ConsoleColor.Green,
                LogLevel.Warning => ConsoleColor.Yellow,
                LogLevel.Error => ConsoleColor.Red,
                LogLevel.Critical => ConsoleColor.Magenta,
                _ => ConsoleColor.White,
            };
        }
        public async Task<string> ExecuteAsync(string goal, int maxRounds = 10)
        {
            var chat = new Conversation()
                .Add(Role.System,
                     "You are an assistant." +
                 "For complex tasks, always Plan and answer step by step" +
                 "if not sure on what to respind, express that to user directly" +
                 "Use relevant tools if needed, or respond directly." +
                 "Provide your answers short and sweet")
                .Add(Role.User, goal);

            chat.OnChat += chat =>
            {
                Log(
                    chat.Role is Role.Assistant or Role.User or Role.Tool ? LogLevel.Information : LogLevel.Debug,
                    nameof(Conversation),
                    $"{chat.Role}: '{(string.IsNullOrWhiteSpace(chat.Content) ? chat.toolCallInfo?.ToString() ?? "<empty>" : chat.Content)}'",
                    chat.Role switch
                    {
                        Role.User => ConsoleColor.Cyan,
                        Role.Assistant => ConsoleColor.Green,
                        Role.Tool => ConsoleColor.Yellow,
                        _ => (ConsoleColor?)null
                    }
                );
            };

            ToolCall toolCall = ToolCall.Empty;
            try
            {
                toolCall = await _coord.GetToolCall(chat);
            }
            catch (Exception ex)
            {
                chat.Add(Role.Assistant, "Error fetching tool call.");
            }

            await ExecuteToolChain(toolCall, chat);

            return chat.LastOrDefault().Content;

        }
        private async Task ExecuteToolChain(ToolCall initialCall, Conversation chat)
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
                    result = await _coord.HandleToolCall(currentToolCall);
                }
                catch (Exception ex)
                {
                    chat.Add(Role.Assistant, $"Tool invocation failed - {ex}");
                    return;
                }

                chat.Add(Role.Tool, result?.ToString(), currentToolCall);

                try
                {
                    currentToolCall = await _coord.GetToolCall(chat);
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
