using Agenty.LLMCore.ChatHandling;
using System;
using System.Linq;

namespace Agenty.AgentCore.TokenHandling
{
    public sealed class SlidingWindowTokenManager : ITokenManager
    {
        public ITokenizer Tokenizer { get; }
        public int MaxTokens { get; }
        private readonly double _safetyMargin;
        private int _lastDropped = 0;

        public SlidingWindowTokenManager(
            ITokenizer? tokenizer = null,
            int maxTokens = 4000,
            double safetyMargin = 0.9)
        {
            Tokenizer = tokenizer ?? new SharpTokenTokenizer(); // default
            MaxTokens = maxTokens;
            _safetyMargin = safetyMargin;
        }

        public void Trim(Conversation convo)
        {
            if (convo == null) throw new ArgumentNullException(nameof(convo));

            int limit = (int)(MaxTokens * _safetyMargin);
            _lastDropped = 0;

            int count = CountTokens(convo);
            if (count <= limit) return;

            // Always keep system messages
            var system = convo.Where(c => c.Role == Role.System).ToList();

            // Drop Tool messages first
            int before = convo.Count;
            convo.RemoveAll(c => c.Role == Role.Tool);
            _lastDropped += before - convo.Count;

            count = CountTokens(convo);
            if (count <= limit) return;

            // Sliding window on User + Assistant
            var core = convo.Where(c => c.Role == Role.User || c.Role == Role.Assistant).ToList();
            while (count > limit && core.Count > 1)
            {
                var oldest = core.First();
                convo.Remove(oldest);
                core.RemoveAt(0);
                _lastDropped++;
                count = CountTokens(convo);
            }

            // Restore system at the top if lost
            foreach (var msg in system)
            {
                if (!convo.Contains(msg))
                    convo.Insert(0, msg);
            }
        }

        public int CountTokens(Conversation convo)
        {
            if (convo == null) throw new ArgumentNullException(nameof(convo));
            var json = convo.ToJson(ChatFilter.All);
            return Tokenizer.Encode(json).Count;
        }
    }
}
