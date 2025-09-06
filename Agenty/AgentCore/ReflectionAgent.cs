using Agenty.LLMCore;
using Agenty.LLMCore.Logging;
using Agenty.LLMCore.Providers.OpenAI;
using Agenty.LLMCore.ToolHandling;
using System;
using System.Text.Json.Nodes;

namespace Agenty.AgentCore
{
    public sealed class ReflectionAgent : IAgent
    {
        private ILLMClient _llm = null!;
        private ToolCoordinator _coord = null!;
        private readonly IToolRegistry _tools = new ToolRegistry();
        Conversation chat = new();
        ILogger _logger = null!;
        Gate? _grader;

        public static ReflectionAgent Create() => new();
        private ReflectionAgent() { }

        public ReflectionAgent WithLLM(string baseUrl, string apiKey, string model = "any_model")
        {
            _llm = new OpenAILLMClient();
            _llm.Initialize(baseUrl, apiKey, model);
            _coord = new ToolCoordinator(_llm, _tools);
            return this;
        }

        public ReflectionAgent WithTools<T>() { _tools.RegisterAll<T>(); return this; }
        public ReflectionAgent WithTools(params Delegate[] fns) { _tools.Register(fns); return this; }
        public ReflectionAgent WithLogger(ILogger logger)
        {
            _logger = logger;
            _grader = new Gate(_coord, _logger);
            _logger.AttachTo(chat);
            return this;
        }

        public async Task<string> ExecuteAsync(string goal, int maxRounds = 50)
        {
            chat.Add(Role.System, "You are a concise QA assistant. Answer in <=3 sentences.")
                .Add(Role.User, goal);

            for (int round = 0; round < maxRounds; round++)
            {
                var response = await _llm.GetResponse(chat);
                chat.Add(Role.Assistant, response);

                var answerGrade = await _grader!.CheckAnswer(goal, chat.ToString(~ChatFilter.System));
                if (answerGrade.confidence_score == Verdict.yes) return response;

                chat.Add(Role.User, answerGrade.explanation);
            }

            chat.Add(Role.User, "Max rounds reached. Return the best final answer now as plain text.");
            return await _llm.GetResponse(chat);
        }
    }
}
