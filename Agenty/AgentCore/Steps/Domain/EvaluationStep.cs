using Agenty.AgentCore.Runtime;
using Agenty.LLMCore;
using Agenty.LLMCore.ChatHandling;

namespace Agenty.AgentCore.Steps.Domain
{
    public enum Verdict { no, partial, yes }
    public record Answer(Verdict confidence_score, string explanation);

    public sealed class EvaluationStep : IAgentStep<string, Answer>
    {
        private readonly string _systemInstruction;
        private readonly bool _injectFeedback;

        public EvaluationStep(
            string? systemInstruction = null,
            bool injectFeedback = false)
        {
            _systemInstruction = systemInstruction ??
                @"You are a strict evaluator. 
                  Judge whether the RESPONSE directly and reasonably addresses the USER REQUEST. 
                  If it only partly addresses it, return 'partial'. 
                  Do not accept vague, off-topic, or padded responses as correct.";

            _injectFeedback = injectFeedback;
        }

        public async Task<Answer?> RunAsync(IAgentContext ctx, string? input = null)
        {
            if (string.IsNullOrWhiteSpace(input))
                throw new ArgumentException("EvaluationStep requires a non-empty summary input.");

            var goal = ctx.Memory.Working.GetCurrentUserRequest()
                       ?? throw new InvalidOperationException("No user input found in conversation.");

            var verdict = await ctx.LLM.GetStructured<Answer>(
                new Conversation()
                    .Add(Role.System, _systemInstruction)
                    .Add(Role.User, $"USER REQUEST: {goal}\n RESPONSE: {input}"),
                LLMMode.Deterministic);

            if (_injectFeedback && !string.IsNullOrWhiteSpace(verdict?.explanation))
                ctx.Memory.Working.Add(Role.User, verdict.explanation);

            return verdict;
        }
    }
}
