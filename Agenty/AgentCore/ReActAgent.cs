using Agenty.LLMCore;
using Agenty.LLMCore.Logging;
using Agenty.LLMCore.Providers.OpenAI;
using Agenty.LLMCore.ToolHandling;
using Microsoft.Extensions.Logging;
using System;
using System.Text.Json.Nodes;
using ILogger = Agenty.LLMCore.Logging.ILogger;

namespace Agenty.AgentCore
{
    public sealed class ReActAgent : IAgent
    {
        private ILLMClient _llm = null!;
        private ToolCoordinator _coord = null!;
        private readonly IToolRegistry _tools = new ToolRegistry();
        Conversation _globalChat;
        Grader? _grader;
        ILogger _logger;
        string _systemPrompt = "You are an assistant. " +
                "For complex tasks, always plan step by step. " +
                "If unsure, say so directly. " +
                "Use tools if needed, or respond directly. " +
                "Keep answers short and clear.";

        public static ReActAgent Create() => new();
        private ReActAgent() { }

        public ReActAgent WithLLM(string baseUrl, string apiKey, string model = "any_model")
        {
            _llm = new OpenAILLMClient();
            _llm.Initialize(baseUrl, apiKey, model);
            _coord = new ToolCoordinator(_llm, _tools);
            _globalChat = new Conversation().Add(Role.System, _systemPrompt);
            return this;
        }

        public ReActAgent WithTools<T>() { _tools.RegisterAll<T>(); return this; }
        public ReActAgent WithTools(params Delegate[] fns) { _tools.Register(fns); return this; }
        public ReActAgent WithLogger(ILogger logger)
        {
            _logger = logger;
            _grader = new Grader(_coord, logger);
            return this;
        }

        public async Task<string> ExecuteAsync(string goal, int maxRounds = 10)
        {
            var sessionChat = new Conversation();

            _logger.AttachTo(sessionChat);

            sessionChat.Add(Role.System, _systemPrompt)
                .Add(Role.User, goal);

            while (true)
            {
                var response = await _coord.GetToolCalls(sessionChat);
                await ExecuteToolChaining(response, sessionChat);

                var sum = await _grader!.SummarizeConversation(sessionChat, goal);
                var answer = await _grader!.CheckAnswer(goal, sum.summary);

                if (answer.verdict == Verdict.yes || answer.confidence_score >= 80)
                {
                    _globalChat.Add(Role.User, goal)
                                .Add(Role.Assistant, sum.summary);
                    return sum.summary;
                }
            }
        }

        private async Task ExecuteToolChaining(LLMResponse response, Conversation chat)
        {
            while (response.ToolCalls.Count != 0)
            {
                await _coord.HandleToolCallSequential(response.ToolCalls, chat);

                response = await _coord.GetToolCalls(chat);
            }
        }

    }
}
