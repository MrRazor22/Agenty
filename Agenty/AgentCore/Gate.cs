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
    public enum Verdict
    {
        [JsonPropertyName("no")]
        no,
        [JsonPropertyName("yes")]
        yes
    }
    public record Answer(Verdict confidence_score, string explanation);
    public record SummaryResult(string summary);

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

        // Common grading gates
        public Task<Answer> CheckAnswer(string goal, Conversation chat) =>
    Grade<Answer>(
        @"You are a strict grader. Decide how well the RESPONSE satisfies the REQUEST.",
        $"REQUEST: {goal}\n RESPONSE: {chat.ToString()}",
        LLMMode.Deterministic
    );


        // ✅ Summarizer gate
        public Task<SummaryResult> SummarizeConversation(Conversation chat, string userRequest)
        {
            var history = chat.ToString(includeSystem: false);

            return Grade<SummaryResult>(
                @"From the conversation provided to you produce a single consolidated final answer 
              that directly responds to the request",
                $"REQUEST: {userRequest}\n CONVERSATION:\n{history}",
                LLMMode.Creative
            );
        }
    }
}
