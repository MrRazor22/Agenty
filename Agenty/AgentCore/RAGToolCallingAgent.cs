using Agenty.LLMCore;
using Agenty.LLMCore.BuiltInTools;
using Agenty.LLMCore.JsonSchema;
using Agenty.LLMCore.Logging;
using Agenty.LLMCore.Providers.OpenAI;
using Agenty.LLMCore.ToolHandling;
using Agenty.RAG;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Agenty.AgentCore
{
    public sealed class RAGToolCallingAgent : IAgent
    {
        private ILLMClient _llm = null!;
        private ToolCoordinator _coord = null!;
        private readonly IToolRegistry _tools = new ToolRegistry();
        private Conversation _globalChat = null!;
        private Gate? _gate;
        private ILogger _logger = null!;

        private const string _systemPrompt =
            "You are a helpful assistant with access to retrieval tools. " +
            "Always prefer using the knowledge base when relevant. " +
            "If no knowledge base results are useful, use web search. " +
            "For custom input text, use the ad-hoc search tool. " +
            "Keep answers short, factual, and always cite sources if available.";

        public static RAGToolCallingAgent Create() => new();

        private RAGToolCallingAgent() { }

        public RAGToolCallingAgent WithLLM(string baseUrl, string apiKey, string model = "any_model")
        {
            _llm = new OpenAILLMClient();
            _llm.Initialize(baseUrl, apiKey, model);
            _coord = new ToolCoordinator(_llm, _tools);
            _globalChat = new Conversation().Add(Role.System, _systemPrompt);
            return this;
        }

        public RAGToolCallingAgent WithLogger(ILogger logger)
        {
            _logger = logger;
            _gate = new Gate(_coord, logger);
            return this;
        }

        public RAGToolCallingAgent WithRAGTools(IRagCoordinator coord, int topK = 3, SearchScope scope = SearchScope.Both)
        {
            // Register only RAG tools
            RAGTools.Initialize(coord, topK, scope);
            _tools.RegisterAll<RAGTools>();
            return this;
        }

        public async Task<string> ExecuteAsync(string goal, int maxRounds = 10)
        {
            var sessionChat = new Conversation();
            _logger.AttachTo(sessionChat);

            sessionChat.Add(Role.System, _systemPrompt)
                       .Add(Role.User, goal);

            for (int round = 0; round < maxRounds; round++)
            {
                var response = await _coord.GetToolCalls(sessionChat);
                await ExecuteToolChaining(response, sessionChat);

                var sum = await _gate!.SummarizeConversation(sessionChat, goal);
                var verdict = await _gate!.CheckAnswer(goal, sum.summariedAnswer);

                if (verdict.confidence_score is Verdict.yes or Verdict.partial)
                {
                    sessionChat.Add(Role.Assistant, sum.summariedAnswer);
                    if (verdict.confidence_score == Verdict.partial)
                        sessionChat.Add(Role.User, verdict.explanation);

                    var final = await _llm.GetResponse(
                        sessionChat.Add(Role.User, "Give a final user friendly answer with sources if possible."),
                        LLMMode.Creative);

                    _globalChat.Add(Role.User, goal)
                               .Add(Role.Assistant, final);

                    return final;
                }

                sessionChat.Add(Role.User, verdict.explanation);
            }

            return await _llm.GetResponse(
                sessionChat.Add(Role.User, $"Answer the user’s request: {goal}. Use the tool results if available."),
                LLMMode.Creative);
        }

        private async Task ExecuteToolChaining(LLMResponse response, Conversation chat)
        {
            while (response.ToolCalls.Count != 0)
            {
                await HandleToolCallSequential(response.ToolCalls, chat);
                response = await _coord.GetToolCalls(chat);
            }
        }

        private async Task HandleToolCallSequential(List<ToolCall> toolCalls, Conversation chat)
        {
            foreach (var call in toolCalls)
            {
                if (string.IsNullOrWhiteSpace(call.Name) && !string.IsNullOrWhiteSpace(call.Message))
                {
                    chat.Add(Role.Assistant, call.Message);
                    continue;
                }

                chat.Add(Role.Assistant, null, toolCall: call);

                try
                {
                    var result = await _coord.Invoke(call);
                    chat.Add(Role.Tool, ((object?)result).AsJSONString(), toolCall: call);
                }
                catch (Exception ex)
                {
                    chat.Add(Role.Tool, $"Tool execution error: {ex.Message}", toolCall: call);
                }
            }
        }
    }
}
