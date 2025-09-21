using Agenty.LLMCore;
using Agenty.LLMCore.ChatHandling;
using Agenty.LLMCore.Messages;

namespace Agenty.AgentCore.TokenHandling
{
    /// <summary>
    /// Summarization policy:
    /// - Always keep System messages
    /// - Summarize earliest User+Assistant into a compressed summary when limit exceeded
    /// - Drop Tool messages first (like sliding window)
    /// </summary>
    public sealed class SummarizingTokenManager : ITokenManager
    {
        public ITokenizer Tokenizer { get; }
        public int MaxTokens { get; }
        private readonly ILLMOrchestrator _llm;

        private int _lastDropped = 0;

        public SummarizingTokenManager(ITokenizer tokenizer, ILLMOrchestrator llm, int maxTokens = 4000)
        {
            Tokenizer = tokenizer ?? throw new ArgumentNullException(nameof(tokenizer));
            _llm = llm ?? throw new ArgumentNullException(nameof(llm));
            MaxTokens = maxTokens;
        }

        public void Trim(Conversation convo, int? maxTokens = null)
        {
            int limit = maxTokens ?? MaxTokens;
            _lastDropped = 0;

            int count = CountTokens(convo.ToJson(ChatFilter.All));
            if (count <= limit) return;

            // Step 1: keep System messages
            var system = convo.Where(c => c.Role == Role.System).ToList();

            // Step 2: drop tool messages
            int before = convo.Count;
            convo.RemoveAll(c => c.Role == Role.Tool);
            _lastDropped += before - convo.Count;

            // Step 3: if still over limit, summarize earliest chunk
            if (CountTokens(convo.ToJson(ChatFilter.All)) > limit)
            {
                SummarizeOldest(convo).GetAwaiter().GetResult(); // blocking call here, but you could make Trim async
            }

            // Reinstate system messages if dropped
            foreach (var msg in system)
                if (!convo.Contains(msg)) convo.Insert(0, msg);
        }

        private async Task SummarizeOldest(Conversation convo)
        {
            // Pick first few User+Assistant messages
            var oldSegment = convo
                .Where(c => c.Role == Role.User || c.Role == Role.Assistant)
                .Take(4) // configurable
                .ToList();

            if (oldSegment.Count < 2) return;

            var historyConv = new Conversation();
            foreach (var c in oldSegment)
                historyConv.Add(c.Role, c.Content);

            // Ask LLM to compress
            var summary = await _llm.GetResponse(
                new Conversation()
                    .Add(Role.System, "Summarize this conversation history as compactly as possible, preserving key facts.")
                    .Append(historyConv, includeSystem: false)
                    .Add(Role.User, "Summarize above into a short context note."),
                LLMMode.Deterministic);

            // Replace old messages with one summary message
            foreach (var c in oldSegment) convo.Remove(c);
            convo.Insert(1, new Chat(Role.Assistant, new TextContent($"[Summary]: {summary}")));
            _lastDropped += oldSegment.Count;
        }

        public TokenUsageReport Report(Conversation convo, int? maxTokens = null)
        {
            int limit = maxTokens ?? MaxTokens;

            int totalTokens = CountTokens(convo.ToJson(ChatFilter.All));
            var roleCounts = convo.GroupBy(c => c.Role).ToDictionary(g => g.Key, g => g.Count());

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
