using Agenty.LLMCore;
using Agenty.LLMCore.JsonSchema;
using Agenty.LLMCore.Logging;
using Agenty.LLMCore.ToolHandling;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Agenty.AgentCore
{
    /// <summary>
    /// Executor that lets the LLM drive Q&A using RAGTools via tool-calling.
    /// </summary>
    public sealed class RAGToolCallingExecutor : IExecutor
    {
        private const string SystemPrompt =
            "You are a helpful assistant with access to retrieval tools. " +
            "Always prefer using the knowledge base when relevant. " +
            "If no knowledge base results are useful, use web search. " +
            "For custom input text, use the ad-hoc search tool. " +
            "Keep answers short, factual, and always cite sources if available.";

        public async Task<string> ExecuteAsync(IAgentContext context, string goal)
        {
            var coord = context.Tools;
            var gate = new Gate(coord, context.Logger);

            var sessionChat = new Conversation().Append(context.GlobalChat);
            context.Logger.AttachTo(sessionChat);

            sessionChat.Add(Role.System, SystemPrompt);

            string answer = "";
            const int maxRounds = 10;

            for (int round = 0; round < maxRounds; round++)
            {
                var response = await coord.GetToolCalls(sessionChat);
                await ExecuteToolChaining(coord, response, sessionChat);

                var sum = await gate.SummarizeConversation(sessionChat, goal);
                var verdict = await gate.CheckAnswer(goal, sum.summariedAnswer);

                if (verdict.confidence_score is Verdict.yes or Verdict.partial)
                {
                    sessionChat.Add(Role.Assistant, sum.summariedAnswer);

                    if (verdict.confidence_score == Verdict.partial)
                        sessionChat.Add(Role.User, verdict.explanation);

                    var final = await context.LLM.GetResponse(
                        sessionChat.Add(Role.User, "Give a final user friendly answer with sources if possible."),
                        LLMMode.Creative);

                    context.GlobalChat.Add(Role.User, goal)
                                      .Add(Role.Assistant, final);

                    answer = final;
                    break;
                }

                sessionChat.Add(Role.User, verdict.explanation);
            }

            if (string.IsNullOrEmpty(answer))
            {
                answer = await context.LLM.GetResponse(
                    sessionChat.Add(Role.User, $"Answer the user’s request: {goal}. Use the tool results if available."),
                    LLMMode.Creative);
            }

            return answer;
        }

        private static async Task ExecuteToolChaining(IToolCoordinator coord, LLMResponse response, Conversation chat)
        {
            while (response.ToolCalls.Count != 0)
            {
                await HandleToolCallSequential(coord, response.ToolCalls, chat);
                response = await coord.GetToolCalls(chat);
            }
        }

        private static async Task HandleToolCallSequential(IToolCoordinator coord, List<ToolCall> toolCalls, Conversation chat)
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
                    var result = await coord.Invoke(call);
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
