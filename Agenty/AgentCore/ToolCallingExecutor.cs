using Agenty.LLMCore;
using Agenty.LLMCore.JsonSchema;
using Agenty.LLMCore.Logging;
using Agenty.LLMCore.ToolHandling;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Agenty.AgentCore
{
    public sealed class ToolCallingExecutor : IExecutor
    {
        private const string _systemPrompt =
            "You are an assistant. " +
            "For complex tasks, plan step by step. " +
            "Use tools if they provide factual data. " +
            "Keep answers short and clear.";

        public async Task<string> ExecuteAsync(IAgentContext context, string goal)
        {
            if (context.LLM == null)
                throw new InvalidOperationException("LLM not configured in AgentContext.");

            var llm = context.LLM;
            var coord = context.Tools;

            var chat = new Conversation().Append(context.GlobalChat);
            context.Logger?.AttachTo(chat);

            chat.Add(Role.System, _systemPrompt);

            var gate = context.Logger != null ? new Gate(coord, context.Logger) : null;

            const int maxRounds = 50;

            for (int round = 0; round < maxRounds; round++)
            {
                var response = await coord.GetToolCalls(chat);
                await ExecuteToolChaining(coord, response, chat);

                if (gate == null)
                    return await llm.GetResponse(chat);

                var sum = await gate.SummarizeConversation(chat, goal);
                var verdict = await gate.CheckAnswer(goal, sum.summariedAnswer);

                if (verdict.confidence_score is Verdict.yes or Verdict.partial)
                {
                    chat.Add(Role.Assistant, sum.summariedAnswer);
                    if (verdict.confidence_score == Verdict.partial)
                        chat.Add(Role.User, verdict.explanation);

                    var final = await llm.GetResponse(
                        chat.Add(Role.User, "Give a final user friendly answer."),
                        LLMMode.Creative);

                    context.GlobalChat?.Add(Role.User, goal).Add(Role.Assistant, final);
                    return final;
                }

                chat.Add(Role.User,
                    verdict.explanation + " If possible, correct this using the tools.",
                    isTemporary: true);
            }

            return await llm.GetResponse(
                chat.Add(Role.User,
                $"Answer clearly: {goal}. Use tool results and reasoning so far."),
                LLMMode.Creative);
        }

        private async Task ExecuteToolChaining(IToolCoordinator coord, LLMResponse response, Conversation chat)
        {
            while (response.ToolCalls.Count != 0)
            {
                await HandleToolCalls(coord, response.ToolCalls, chat);
                response = await coord.GetToolCalls(chat);
            }
        }

        private async Task HandleToolCalls(IToolCoordinator coord, List<ToolCall> toolCalls, Conversation chat)
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
