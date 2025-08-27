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
    public record SummaryResult(string summary);
    public record ToolChoiceGrade(Verdict verdict, string explanation, string? recommendedTool);

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

        // ✅ Summarizer gate
        public Task<SummaryResult> SummarizeConversation(Conversation chat)
        {
            var firstUserMessage = chat.FirstOrDefault(c => c.Role == Role.User)?.Content ?? "<no user input>";
            var history = string.Join("\n", chat
                .Skip(1) // everything after first user
                .Select(c => $"{c.Role}: {c.Content ?? c.toolCallInfo?.ToString() ?? "<empty>"}"));

            return Grade<SummaryResult>(
                @"You are summarizing the conversation to produce a single consolidated final answer 
              that directly responds to the INITIAL USER MESSAGE.
              - Use all relevant assistant replies and tool results. 
              - Keep it concise and clear.
              - Ignore irrelevant noise or logging artifacts.",
                $"INITIAL USER MESSAGE: {firstUserMessage}\nCONVERSATION:\n{history}"
            );
        }

        // ✅ Tool Choice Grader (Router Gate) 
        public async Task<ToolChoiceGrade> CheckToolChoice(
     Conversation chat, ToolCall chosenTool, List<Tool> tools)
        {
            var summary = await SummarizeConversation(chat);
            var userRequest = chat.FirstOrDefault(c => c.Role == Role.User)?.Content ?? "<no user input>";
            var toolsDescription = tools?.ToString() ?? "<no tools registered>";

            return await Grade<ToolChoiceGrade>(
                @"You are grading whether the TOOL CHOICE is the most relevant for the CURRENT CONTEXT of the conversation.

Verdict rules:
- Yes → Tool is the best available match for the current step.
- No  → Tool is irrelevant, suboptimal, or misses the obvious better option.

Response schema:
- verdict: Yes | No
- explanation: reasoning
- recommendedTool: 
    - If verdict = No → name of the better tool from the available list
    - If verdict = Yes → empty string

Available tools:
" + toolsDescription,
                $"USER REQUEST: {userRequest}\nSUMMARY OF CONTEXT: {summary.summary}\nCHOSEN TOOL: {chosenTool}"
            );
        }
    }
}
