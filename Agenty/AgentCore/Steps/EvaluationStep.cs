using Agenty.AgentCore.Runtime;
using Agenty.LLMCore;
using Agenty.LLMCore.ChatHandling;

namespace Agenty.AgentCore.Steps
{
    public enum Verdict { no, partial, yes }
    public record Answer(Verdict confidence_score, string explanation);

    public sealed class EvaluationStep : IAgentStep<string, Answer>
    {
        public async Task<Answer?> RunAsync(
            Conversation chat, ILLMCoordinator llm, string? input = null)
        {
            if (string.IsNullOrWhiteSpace(input))
                throw new ArgumentException("EvaluationStep requires a non-empty summary input.");

            var goal = chat.GetLastUserMessage()
                       ?? throw new InvalidOperationException("No user input found in conversation.");

            var verdict = await llm.GetStructured<Answer>(
                new Conversation()
                    .Add(Role.System,
                        @"You are a strict evaluator. 
                          Judge whether the RESPONSE directly and reasonably addresses the USER REQUEST. 
                          If it only partly addresses it, return 'partial'. 
                          Do not accept vague, off-topic, or padded responses as correct.")
                    .Add(Role.User, $"USER REQUEST: {goal}\n RESPONSE: {input}"),
                LLMMode.Deterministic);

            return verdict;
        }
    }
}
