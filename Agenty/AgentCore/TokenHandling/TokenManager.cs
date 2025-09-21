using Agenty.LLMCore.ChatHandling;
using System.Text;

namespace Agenty.AgentCore.TokenHandling
{
    public record TokenUsageReport(
        int TotalTokens,
        int MaxTokens,
        IReadOnlyDictionary<Role, int> RoleCounts,
        int DroppedCount,
        bool WasTrimmed
    );
    public interface ITokenManager
    {
        void Trim(Conversation convo, int? maxTokens = null);
        TokenUsageReport Report(Conversation convo, int? maxTokens = null);
        int CountTokens(string text);
        int MaxTokens { get; }
        ITokenizer Tokenizer { get; }
    }

}
