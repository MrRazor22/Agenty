using Agenty.LLMCore.JsonSchema;
using Agenty.LLMCore.Logging;
using Agenty.LLMCore.ToolHandling;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Threading.Tasks;
using ILogger = Agenty.LLMCore.Logging.ILogger;

namespace Agenty.LLMCore
{
    public enum Verdict { Yes, No }

    public record AnswerGrade(Verdict verdict, string explanation);
    public record PlanGrade(Verdict verdict, string explanation);
    public record ToolArgsGrade(Verdict verdict, string explanation);

    class Grader
    {
        private readonly IToolCoordinator _coord;
        private readonly ILogger _logger;

        public Grader(ToolCoordinator coord, ILogger logger = null)
        {
            _coord = coord;
            _logger = logger;
        }

        // Generic structured grading helper
        private async Task<T> Grade<T>(string systemPrompt, string userPrompt)
        {
            var gateChat = new Conversation()
               .Add(Role.System, systemPrompt)
               .Add(Role.User, userPrompt);

            var result = await _coord.GetStructuredResponse<T>(gateChat);
            _logger?.Log(LogLevel.Debug, $"{typeof(T).Name}", result.AsString());
            return result;
        }

        // Common grading gates
        public Task<AnswerGrade> CheckAnswer(string goal, string response) =>
            Grade<AnswerGrade>(
                @"You are grading whether the ASSISTANT RESPONSE appropriately satisfies the USER REQUEST.
                Verdict rules:
                - Yes → Fully satisfies request OR clearly explains why not possible.
                - No  → Wrong, incomplete, vague, misleading, or unjustified refusal.
                Always explain your reasoning.",
                $"USER REQUEST: {goal}\nASSISTANT RESPONSE: {response}"
                );

        public Task<PlanGrade> CheckPlan(string plan) =>
            Grade<PlanGrade>(
                @"You are grading the ASSISTANT PLAN for clarity and executability.
                Verdict rules:
                - Yes → Steps are clear, actionable, logically ordered.
                - No  → Steps missing, vague, or illogical.
                Always explain your reasoning.",
                $"PLAN: {plan}");

        public Task<ToolArgsGrade> CheckToolArgs(string toolName, string argsJson) =>
            Grade<ToolArgsGrade>(
                @"You are grading whether the TOOL ARGUMENTS are valid and make sense.
                Verdict rules:
                - Yes → Arguments are well-formed, valid types, realistic values.
                - No  → Invalid types, nonsense values, or incomplete args.
                Always explain your reasoning.",
                $"TOOL: {toolName}\nARGS: {argsJson}");
    }
}
