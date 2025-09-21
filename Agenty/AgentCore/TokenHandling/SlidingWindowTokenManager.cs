using Agenty.LLMCore.ChatHandling;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Agenty.AgentCore.TokenHandling
{
    /// <summary>
    /// Sliding Window policy:
    /// - Keep all System messages (never trimmed)
    /// - Drop Tool messages first
    /// - Then apply sliding window on User/Assistant
    /// </summary> 
    public sealed class SlidingWindowTokenManager : ITokenManager
    {
        public ITokenizer Tokenizer { get; }
        public int MaxTokens { get; }

        private int _lastDropped = 0;

        public SlidingWindowTokenManager(ITokenizer tokenizer, int maxTokens = 4000)
        {
            Tokenizer = tokenizer ?? throw new ArgumentNullException(nameof(tokenizer));
            MaxTokens = maxTokens;
        }

        public void Trim(Conversation convo, int? maxTokens = null)
        {
            int limit = maxTokens ?? MaxTokens;
            _lastDropped = 0;

            int count = CountTokens(convo.ToJson(ChatFilter.All));
            if (count <= limit) return;

            // Keep System messages
            var system = convo.Where(c => c.Role == Role.System).ToList();

            // Drop Tool messages first
            int before = convo.Count;
            convo.RemoveAll(c => c.Role == Role.Tool);
            _lastDropped += before - convo.Count;
            if (count <= limit) return;

            // Sliding window on User + Assistant
            var core = convo.Where(c => c.Role == Role.User || c.Role == Role.Assistant).ToList();
            while (count > limit && core.Count > 1)
            {
                var oldest = core.First();
                convo.Remove(oldest);
                core.RemoveAt(0);
                _lastDropped++;
                count = CountTokens(convo.ToJson(ChatFilter.All));
            }

            // Reinsert system if lost
            foreach (var msg in system)
            {
                if (!convo.Contains(msg))
                    convo.Insert(0, msg);
            }
        }

        public TokenUsageReport Report(Conversation convo, int? maxTokens = null)
        {
            int limit = maxTokens ?? MaxTokens;

            int totalTokens = CountTokens(convo.ToJson(ChatFilter.All));
            var roleCounts = convo
                .GroupBy(c => c.Role)
                .ToDictionary(g => g.Key, g => g.Count());

            bool wasTrimmed = totalTokens > limit;

            return new TokenUsageReport(
                TotalTokens: totalTokens,
                MaxTokens: limit,
                RoleCounts: roleCounts,
                DroppedCount: _lastDropped,
                WasTrimmed: wasTrimmed
            );
        }

        public int CountTokens(string text) => Tokenizer.Encode(text).Count;
    }
}
