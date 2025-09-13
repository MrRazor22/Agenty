using Agenty.LLMCore;
using Agenty.LLMCore.Logging;
using Agenty.LLMCore.ToolHandling;
using System;
using System.Threading.Tasks;

namespace Agenty.AgentCore
{
    /// <summary>
    /// Executor that makes the LLM reflect on its answers,
    /// improving them step by step until the grader is satisfied.
    /// </summary>
    public sealed class ReflectionExecutor : IExecutor
    {
        public async Task<string> ExecuteAsync(IAgentContext context, string goal)
        {
            var chat = new Conversation().Append(context.GlobalChat);
            context.Logger.AttachTo(chat);

            chat.Add(Role.System, "You are a concise QA assistant. Answer in <=3 sentences.");

            var grader = new Gate(context.Tools, context.Logger);

            const int maxRounds = 10; // hard safety cap

            for (int round = 0; round < maxRounds; round++)
            {
                var response = await context.LLM.GetResponse(chat);
                chat.Add(Role.Assistant, response);

                var grade = await grader.CheckAnswer(goal, chat.ToString(~ChatFilter.System));
                if (grade.confidence_score == Verdict.yes)
                {
                    return response;
                }

                chat.Add(Role.User, grade.explanation);
            }

            // fallback if no high-confidence answer found
            chat.Add(Role.User, "Max rounds reached. Return the best final answer now as plain text.");
            return await context.LLM.GetResponse(chat);
        }
    }
}
