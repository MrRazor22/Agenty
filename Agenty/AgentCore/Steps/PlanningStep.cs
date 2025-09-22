using Agenty.AgentCore.Runtime;
using Agenty.LLMCore;
using Agenty.LLMCore.ChatHandling;

namespace Agenty.AgentCore.Steps
{
    // A simple structured plan
    public record Plan(List<string> steps, string rationale);

    public sealed class PlanningStep : IAgentStep<object, Plan>
    {
        private readonly string _systemPrompt;

        public PlanningStep(string systemPrompt = "You are a planner. Break down tasks.")
        {
            _systemPrompt = systemPrompt;
        }

        public async Task<Plan?> RunAsync(IAgentContext ctx, object? input = null)
        {
            var goal = ctx.Memory.Working.GetCurrentUserRequest()
                       ?? throw new InvalidOperationException("No user goal found.");

            var plan = await ctx.LLM.GetStructured<Plan>(
                new Conversation()
                    .Add(Role.System, _systemPrompt)
                    .Add(Role.User, $"Goal: {goal}\nContext: {input ?? ctx.Memory.Working.ToJson(~ChatFilter.System)}"),
                LLMMode.Creative);

            ctx.Memory.Working.Add(Role.Assistant, $"Planned: {string.Join(", ", plan.steps)}");
            //throw new Exception("WTF");
            return plan;
        }
    }

    public sealed class ReplanningStep : IAgentStep<Answer, Plan>
    {
        private readonly string _systemPrompt;

        public ReplanningStep(string systemPrompt = "You are a replanner. Adjust strategy based on feedback.")
        {
            _systemPrompt = systemPrompt;
        }

        public async Task<Plan?> RunAsync(IAgentContext ctx, Answer? feedback = null)
        {
            var goal = ctx.Memory.Working.GetCurrentUserRequest()
                       ?? throw new InvalidOperationException("No user goal found.");

            var plan = await ctx.LLM.GetStructured<Plan>(
                new Conversation()
                    .Add(Role.System, _systemPrompt)
                    .Add(Role.User, $"Goal: {goal}\nFeedback: {feedback?.explanation ?? "None"}"),
                LLMMode.Creative);

            ctx.Memory.Working.Add(Role.Assistant, $"Replanned: {string.Join(", ", plan.steps)}");
            return plan;
        }
    }
}
