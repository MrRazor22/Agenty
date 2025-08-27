
using Agenty.LLMCore;
using Agenty.LLMCore.Providers.OpenAI;
using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;
using ILogger = Agenty.LLMCore.ILogger;

namespace Agenty.AgentCore
{
    public sealed class ReActToolCallingAgent : IAgent
    {
        private ILLMClient _llm = null!;
        private ToolCoordinator _coord = null!;
        private readonly IToolRegistry _tools = new ToolRegistry();
        Conversation chat = new();
        ILogger _logger = null!;

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
        public ReActToolCallingAgent WithLogger(ILogger logger)
        {
            _logger = logger;
            logger.AttachTo(chat);
            return this;
        }
        public async Task<string> ExecuteAsync(string goal, int maxRounds = 10)
        {
            chat.Add(Role.System,
                "You are an assistant. " +
                "For complex tasks, always plan step by step. " +
                "If unsure, say so directly. " +
                "Use tools if needed, or respond directly. " +
                "Keep answers short and clear.")
                .Add(Role.User, goal);

            ToolCall toolCall;
            toolCall = await _coord.GetToolCall(chat);

            await ExecuteToolChaining(toolCall, chat);
            return chat.LastOrDefault()?.Content ?? "";
        }

        private async Task ExecuteToolChaining(ToolCall call, Conversation chat)
        {
            while (call != ToolCall.Empty)
            {
                chat.Add(Role.Assistant, call.AssistantMessage);

                if (string.IsNullOrWhiteSpace(call.Name)) break;//no tool call so model returned final resposne

                chat.Add(Role.Assistant, tool: call);

                object? result = await _coord.HandleToolCall(call);

                chat.Add(Role.Tool, result?.ToString(), call);

                call = await _coord.GetToolCall(chat);
            }
        }

    }
}
