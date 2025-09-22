using Agenty.LLMCore;
using Agenty.LLMCore.ChatHandling;

namespace Agenty.AgentCore.Steps
{
    // A simple structured plan
    public record Plan(List<string> steps, string rationale);

    public sealed class PlanningStep : IAgentStep<string, Plan>
    {

        public async Task<Plan?> RunAsync(
            Conversation chat, ILLMOrchestrator llm, string? input = null)
        {
            var plan = await llm.GetStructured<Plan>(
                new Conversation()
                    .Add(Role.System, "You are a planner. Break down tasks.")
                    .Add(Role.User, $"Goal: {chat.GetLastUserMessage()}\nContext: {input ?? chat.ToJson(~ChatFilter.System)}"),
                LLMMode.Creative);

            chat.Add(Role.Assistant, $"Planned: {string.Join(", ", plan.steps)}");

            return plan;
        }
    }

    public sealed class ReplanningStep : IAgentStep<Answer, Plan>
    {
        public async Task<Plan?> RunAsync(
            Conversation chat, ILLMOrchestrator llm, Answer? feedback = null)
        {
            var plan = await llm.GetStructured<Plan>(
                new Conversation()
                    .Add(Role.System, "You are a replanner. Adjust strategy based on feedback.")
                    .Add(Role.User, $"Goal: {chat.GetLastUserMessage()}\nFeedback: {feedback?.explanation ?? "None"}"),
                LLMMode.Creative);

            chat.Add(Role.Assistant, $"Replanned: {string.Join(", ", plan.steps)}");

            return plan;
        }
    }
}
