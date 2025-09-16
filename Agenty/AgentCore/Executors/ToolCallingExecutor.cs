using Agenty.LLMCore;
using Agenty.LLMCore.ChatHandling;

namespace Agenty.AgentCore.Executors
{
    public sealed class ToolCallingExecutor : IExecutor
    {
        public async Task<string> ExecuteAsync(IAgentContext context, string goal)
        {
            if (context.LLM == null)
                throw new InvalidOperationException("LLM not configured in AgentContext.");

            var llm = context.LLM;
            var coord = context.Tools;

            var chat = new Conversation().Append(context.Conversation);
            context.Logger?.AttachTo(chat);

            IAnswerEvaluator evaluator = context.Logger != null ? new AnswerEvaluator(coord, context.Logger) : null;

            const int maxRounds = 50;

            for (int round = 0; round < maxRounds; round++)
            {
                var response = await coord.GetToolCalls(chat);

                while (response.ToolCalls.Count != 0)
                {
                    await coord.RunToolCalls(response.ToolCalls, chat);
                    response = await coord.GetToolCalls(chat);
                }

                if (evaluator == null)
                    return await llm.GetResponse(chat);

                var sum = await evaluator.Summarize(chat, goal);
                var verdict = await evaluator.EvaluateAnswer(goal, sum.summariedAnswer);

                if (verdict.confidence_score is Verdict.yes or Verdict.partial)
                {
                    chat.Add(Role.Assistant, sum.summariedAnswer);
                    if (verdict.confidence_score == Verdict.partial)
                        chat.Add(Role.User, verdict.explanation);

                    var final = await llm.GetResponse(
                        chat.Add(Role.User, "Give a final user friendly answer."),
                        LLMMode.Creative);

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
    }
}
