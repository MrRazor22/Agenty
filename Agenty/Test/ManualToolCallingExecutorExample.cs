using Agenty.AgentCore;
using Agenty.AgentCore.Steps;
using Agenty.LLMCore;
using Agenty.LLMCore.ChatHandling;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Agenty.Test
{
    public sealed class ToolCallingExecutor : IExecutor
    {
        private readonly int _maxRounds;
        public ToolCallingExecutor(int maxRounds = 50) => _maxRounds = maxRounds;

        public async Task<object?> Execute(IAgentContext ctx)
        {
            var chat = new Conversation().Append(ctx.Memory.Working);
            ctx.Logger.AttachTo(chat);
            var llm = ctx.LLM;

            for (int round = 0; round < _maxRounds; round++)
            {
                // loop tool calls until none left
                for (var resp = await llm.GetToolCallResponse(chat);
                     resp.Calls.Count > 0;
                     resp = await llm.GetToolCallResponse(chat))
                {
                    chat.AppendToolResults(await llm.RunToolCalls(resp.Calls.ToList()));
                }

                // summarization
                var summaryResult = await new SummarizationStep().RunAsync(ctx);
                if (string.IsNullOrEmpty(summaryResult)) continue;

                // evaluation
                var verdict = await new EvaluationStep().RunAsync(ctx);

                if (verdict?.confidence_score is Verdict.yes or Verdict.partial)
                {
                    if (verdict.confidence_score == Verdict.partial)
                        chat.Add(Role.User, verdict.explanation);

                    // finalization (step-based, typed)
                    var finalResult = await new FinalizeStep().RunAsync(ctx);
                    return finalResult;
                }

                // push correction loop
                chat.Add(Role.User, (verdict.explanation ?? "Try again.") + " If possible, correct this using the tools.");
            }

            ctx.Logger.LogUsage(ctx.TokenManager.Report(chat), "Session Token Usage");
            // fallback (no step here, just plain response)
            return await llm.GetResponse(chat.Add(Role.User, "Answer clearly using all reasoning so far."), LLMMode.Creative);
        }
    }
}
