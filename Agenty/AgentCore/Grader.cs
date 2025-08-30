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
    public enum Verdict { Yes, No }
    public record AnswerGrade(Verdict verdict, string explanation);
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
            _logger?.Log(LogLevel.Debug, $"{typeof(T).Name}", result.AsString());
            return result;
        }

        // Common grading gates
        public Task<AnswerGrade> CheckAnswer(string goal, string response) =>
            Grade<AnswerGrade>(
                @"You are critiquing assistant, who is tasked to check whether the ASSISTANT RESPONSE appropriately satisfies the USER REQUEST.",
                $"USER REQUEST: {goal}\nASSISTANT RESPONSE: {response}"
                );

        // ✅ Summarizer gate
        public Task<SummaryResult> SummarizeConversation(Conversation chat, string userRequest)
        {
            var history = chat.ToHistoyString(includeSystem: false);

            return Grade<SummaryResult>(
                @"You are summarizing the agent response to produce a single consolidated final answer 
              that directly responds to the USER REQUEST.
              - Use all relevant facts and tool results. 
              - Make it user friendly.",
                $"USER REQUEST: {userRequest}\nASSISTANT RESPONSE:\n{history}"
            );
        }
    }
}
