using Agenty.LLMCore;
using System.Text;
using static System.Net.Mime.MediaTypeNames;

namespace Agenty.AgentCore.TokenHandling
{
    public interface ITokenManager
    {
        void Trim(Conversation convo, int maxTokens);
        TokenUsageReport Report(Conversation convo, int maxTokens);
        int CountTokens(string text);

        ITokenizer Tokenizer { get; }
    }

    public record TokenUsageReport(
        int TotalTokens,
        int MaxTokens,
        IReadOnlyDictionary<Role, int> RoleCounts,
        int TempCount,
        int DroppedCount,
        bool WasTrimmed
    )
    {
        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append($"[Tokens] {TotalTokens}/{MaxTokens} (trimmed={WasTrimmed})");

            foreach (var kv in RoleCounts)
                sb.Append($", {kv.Key}={kv.Value}");

            if (TempCount > 0)
                sb.Append($", Temp={TempCount}");

            if (DroppedCount > 0)
                sb.Append($", Dropped≈{DroppedCount}");

            return sb.ToString();
        }
    }

    /// <summary>
    /// Default policy:
    /// - Keep all System + Backstory (never trimmed)
    /// - Drop Tool + temporary chats first
    /// - Then apply sliding window on User/Assistant
    /// </summary> 
    public sealed class DefaultTokenManager : ITokenManager
    {
        public ITokenizer Tokenizer { get; }
        private int _lastDropped = 0;

        public DefaultTokenManager(ITokenizer tokenizer)
        {
            Tokenizer = tokenizer ?? throw new ArgumentNullException(nameof(tokenizer));
        }

        public void Trim(Conversation convo, int maxTokens)
        {
            _lastDropped = 0;
            int count = CountTokens(convo.ToString(ChatFilter.All));
            if (count <= maxTokens) return;

            // Keep System messages
            var system = convo.Where(c => c.Role == Role.System).ToList();

            // Drop Tool first
            int before = convo.Count;
            convo.RemoveAll(c => c.Role == Role.Tool);
            _lastDropped += before - convo.Count;
            if (count <= maxTokens) return;

            // Sliding window on User + Assistant
            var core = convo.Where(c => c.Role == Role.User || c.Role == Role.Assistant).ToList();
            while (count > maxTokens && core.Count > 1)
            {
                var oldest = core.First();
                convo.Remove(oldest);
                core.RemoveAt(0);
                _lastDropped++;
            }

            // Reinsert system if lost
            foreach (var msg in system)
            {
                if (!convo.Contains(msg))
                    convo.Insert(0, msg);
            }
        }

        public TokenUsageReport Report(Conversation convo, int maxTokens)
        {
            int totalTokens = CountTokens(convo.ToString(ChatFilter.All));
            var roleCounts = convo
                .GroupBy(c => c.Role)
                .ToDictionary(g => g.Key, g => g.Count());

            bool wasTrimmed = totalTokens > maxTokens;

            return new TokenUsageReport(
                TotalTokens: totalTokens,
                MaxTokens: maxTokens,
                RoleCounts: roleCounts,
                TempCount: convo.Count(c => c.IsTemporary),
                DroppedCount: _lastDropped,
                WasTrimmed: wasTrimmed
            );
        }

        public int CountTokens(string text) => Tokenizer.Encode(text).Count;
    }
}
