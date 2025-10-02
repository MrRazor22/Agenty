using Agenty.LLMCore.ChatHandling;
using System.Collections.Generic;
using System.Text;

namespace Agenty.AgentCore.TokenHandling
{
    public class TokenUsageReport
    {
        public int TotalTokens { get; }
        public int MaxTokens { get; }
        public IReadOnlyDictionary<Role, int> RoleCounts { get; }
        public int DroppedCount { get; }
        public bool WasTrimmed { get; }

        public TokenUsageReport(
            int totalTokens,
            int maxTokens,
            IReadOnlyDictionary<Role, int> roleCounts,
            int droppedCount,
            bool wasTrimmed
        )
        {
            TotalTokens = totalTokens;
            MaxTokens = maxTokens;
            RoleCounts = roleCounts;
            DroppedCount = droppedCount;
            WasTrimmed = wasTrimmed;
        }
    }

    public interface ITokenManager
    {
        void Trim(Conversation convo, int? maxTokens = null);
        TokenUsageReport Report(Conversation convo, int? maxTokens = null);
        int CountTokens(string text);
        int MaxTokens { get; }
        ITokenizer Tokenizer { get; }
    }

}
