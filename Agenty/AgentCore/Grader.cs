using Agenty.LLMCore;
using Agenty.LLMCore.JsonSchema;
using Agenty.LLMCore.Logging;
using Agenty.LLMCore.ToolHandling;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Threading.Tasks;
using ILogger = Agenty.LLMCore.Logging.ILogger;

namespace Agenty.AgentCore
{
    public enum Verdict { yes, no }
    public record Answer(Verdict verdict, string explanation, int confidence_score);
    public record SummaryResult(string summary);

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
            _logger?.Log(LogLevel.Debug, $"{typeof(T).Name}", result.AsJSONString());
            return result;
        }

        // Common grading gates
        public Task<Answer> CheckAnswer(string goal, string response) =>
            Grade<Answer>(
                @"You are reviewer, check whether the ASSISTANT RESPONSE acceptable answer for the USER REQUEST.",
                $"USER REQUEST: {goal}\nASSISTANT RESPONSE: {response}"
                );

        // ✅ Summarizer gate
        public Task<SummaryResult> SummarizeConversation(Conversation chat, string userRequest)
        {
            var history = chat.ToHistoyString(includeSystem: false);

            return Grade<SummaryResult>(
                @"You are summarizing the assistant response to produce a single consolidated final answer 
              that directly responds to the USER REQUEST.
              - Use all relevant facts and tool results. 
              - Make it user friendly.",
                $"USER REQUEST: {userRequest}\nASSISTANT RESPONSE:\n{history}"
            );
        }
    }
}
