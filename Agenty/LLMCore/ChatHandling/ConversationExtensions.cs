using Agenty.AgentCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Agenty.LLMCore.ChatHandling
{
    public static class ConversationExtensions
    {
        public static async Task<bool> TryFinalizeAnswer(this Conversation chat,
            IAnswerEvaluator eval, ILLMClient llm, string goal)
        {
            var sum = await eval.Summarize(chat, goal);
            var verdict = await eval.EvaluateAnswer(goal, sum.summariedAnswer);

            if (verdict.confidence_score is Verdict.yes or Verdict.partial)
            {
                chat.Add(Role.Assistant, sum.summariedAnswer);
                if (verdict.confidence_score == Verdict.partial)
                    chat.Add(Role.User, verdict.explanation);

                var final = await llm.GetResponse(
                    chat.Add(Role.User, "Give a final user friendly answer."),
                    LLMMode.Creative);

                chat.Add(Role.Assistant, final);
                return true;
            }

            chat.Add(Role.User, verdict.explanation + " If possible, correct this using the tools.", isTemporary: true);
            return false;
        }
    }

}
