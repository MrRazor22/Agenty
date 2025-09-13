using Agenty.LLMCore;
using Agenty.LLMCore.JsonSchema;
using Agenty.LLMCore.Logging;
using Agenty.LLMCore.ToolHandling;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using ILogger = Agenty.LLMCore.Logging.ILogger;

namespace Agenty.AgentCore
{
    public enum Verdict { no, partial, yes }
    public record Answer(Verdict confidence_score, string explanation);
    public record SummaryResult(string summariedAnswer);

    class Gate
    {
        private readonly IToolCoordinator _coord;
        private readonly ILogger _logger;

        public Gate(ToolCoordinator coord, ILogger logger = null)
        {
            _coord = coord;
            _logger = logger;
        }


        // Generic structured grading helper
        private async Task<T> Grade<T>(string systemPrompt, string userPrompt, LLMMode mode)
        {
            var gateChat = new Conversation()
               .Add(Role.System, systemPrompt)
               .Add(Role.User, userPrompt);

            var result = await _coord.GetStructuredResponse<T>(gateChat, mode: mode);
            _logger?.Log(LogLevel.Debug, $"{typeof(T).Name}", result.AsJSONString());
            return result;
        }

        public Task<Answer> CheckAnswer(string goal, string response) =>
    Grade<Answer>(
        @"You are a strict evaluator. 
        Judge whether the RESPONSE directly and reasonably addresses the USER REQUEST. 
        If it only partly addresses it, return 'partial'. 
        Do not accept vague, off-topic, or padded responses as correct.",
        $"USER REQUEST: {goal}\n RESPONSE: {response}",
        LLMMode.Deterministic
    );

        public Task<SummaryResult> SummarizeConversation(Conversation chat, string userRequest)
        {
            var history = chat.ToString(~ChatFilter.System);

            return Grade<SummaryResult>(
                @"Provide a concise direct final answer only addressing the current USER QUESTION using only the CONTEXT. 
Always include any relevant observations from the CONTEXT, even if they do not fully answer the question. 
If the information is incomplete, state the limitation clearly."
,
                $"USER QUESTION: {userRequest}\nCONTEXT:\n{history}",
                LLMMode.Creative
            );
        }


    }
}
