using Agenty.LLMCore;
using Agenty.LLMCore.Providers.OpenAI;
using Agenty.LLMCore.ToolHandling;
using System;
using System.Collections.Generic;
using Agenty.LLMCore;
using Agenty.LLMCore.Providers.OpenAI;
using Agenty.LLMCore.ToolHandling;
using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;
using ILogger = Agenty.LLMCore.Logging.ILogger;

namespace Agenty.AgentCore
{
    public sealed class GatedReActAgent : IAgent
    {
        private ILLMClient _llm = null!;
        private ToolCoordinator _coord = null!;
        private readonly IToolRegistry _tools = new ToolRegistry();
        Conversation chat = new();
        ILogger _logger = null!;
        Grader? _grader;
        string _currentGoal;

        public static GatedReActAgent Create() => new();
        private GatedReActAgent() { }

        public GatedReActAgent WithLLM(string baseUrl, string apiKey, string model)
        {
            _llm = new OpenAILLMClient();
            _llm.Initialize(baseUrl, apiKey, model);
            _coord = new ToolCoordinator(_llm, _tools);
            return this;
        }

        public GatedReActAgent WithTools<T>() { _tools.RegisterAll<T>(); return this; }
        public GatedReActAgent WithTools(params Delegate[] fns) { _tools.Register(fns); return this; }
        public GatedReActAgent WithLogger(ILogger logger)
        {
            _logger = logger;
            _grader = new Grader(_coord, _logger);
            _logger.AttachTo(chat);
            return this;
        }
        public async Task<string> ExecuteAsync(string goal, int maxRounds = 10)
        {
            _currentGoal = goal;
            chat.Add(Role.System,
                "You are an assistant. " +
                "For complex tasks, always plan step by step. " +
                "If unsure, say so directly. " +
                "Use tools if needed, or respond directly. " +
                "Keep answers short and clear.")
                .Add(Role.User, _currentGoal);

            ToolCall toolCall;
            toolCall = await _coord.GetToolCall(chat);

            return await ExecuteToolChaining(toolCall, chat);
        }

        private async Task<string> ExecuteToolChaining(ToolCall call, Conversation chat)
        {
            while (call != ToolCall.Empty)
            {
                if (string.IsNullOrWhiteSpace(call.Name))
                {
                    var response = await _grader.SummarizeConversation(chat);

                    var answerGrade = await _grader!.CheckAnswer(_currentGoal, response.summary);
                    if (answerGrade.verdict == Verdict.Yes) return response.summary;

                    chat.Add(Role.User, answerGrade.explanation);
                }

                await _coord.HandleToolCall(call, chat);

                call = await _coord.GetToolCall(chat);
            }

            return await _llm.GetResponse(chat);
        }

    }
}
