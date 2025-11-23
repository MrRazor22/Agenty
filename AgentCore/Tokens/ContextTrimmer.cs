using AgentCore.Chat;
using SharpToken;
using System;
using System.Linq;
using System.Reflection;

namespace AgentCore.Tokens
{
    public sealed class ContextTrimOptions
    {
        public int MaxContextTokens { get; set; } = 8000;
        public double Margin { get; set; } = 0.8;
        public string? TokenizerModel { get; set; } = null;
    }

    public interface IContextTrimmer
    {
        Conversation Trim(Conversation convo, int? maxTokens = null, string? model = null);
        int Estimate(Conversation convo, string? model = null);
    }

    internal sealed class SlidingWindowTrimmer : IContextTrimmer
    {
        private readonly ITokenizer _tokenizer;
        private readonly ContextTrimOptions _opts;

        public SlidingWindowTrimmer(ITokenizer tokenizer, ContextTrimOptions opts)
        {
            _tokenizer = tokenizer ?? throw new ArgumentNullException(nameof(tokenizer));
            _opts = opts ?? throw new ArgumentNullException(nameof(opts));

            if (_opts.MaxContextTokens <= 0)
                _opts.MaxContextTokens = 8000;

            if (_opts.Margin <= 0)
                _opts.Margin = 1.0;
        }

        public Conversation Trim(
            Conversation convo,
            int? maxTokens = null,
            string? model = null)
        {
            if (convo == null)
                throw new ArgumentNullException(nameof(convo));

            // ---- Determine limits ----
            int effectiveMax = maxTokens ?? _opts.MaxContextTokens;
            int limit = (int)(effectiveMax * _opts.Margin);
            string tokenizerModel = _opts.TokenizerModel ?? model;

            // We always work on a clone (NON-mutating behavior)
            var trimmed = convo.Clone();

            int count = Estimate(trimmed, tokenizerModel);
            if (count <= limit)
                return trimmed;

            // ---- Keep system messages ----
            var systemMessages = trimmed
                .Where(c => c.Role == Role.System)
                .ToList();

            // ---- Remove tool noise ----
            trimmed.RemoveAll(c => c.Role == Role.Tool);

            count = Estimate(trimmed, tokenizerModel);
            if (count <= limit)
                return trimmed;

            // ---- Sliding window for user/assistant context ----
            var core = trimmed
                .Where(c => c.Role == Role.User || c.Role == Role.Assistant)
                .ToList();

            int idx = 0;
            while (count > limit && idx < core.Count - 1) // keep at least 1 turn
            {
                trimmed.Remove(core[idx]);
                idx++;
                count = Estimate(trimmed, tokenizerModel);
            }

            // ---- Ensure system messages remain at the top ----
            foreach (var sys in systemMessages)
            {
                if (!trimmed.Contains(sys))
                    trimmed.Insert(0, sys);
            }

            return trimmed;
        }

        public int Estimate(Conversation convo, string? model = null)
        {
            if (convo == null) throw new ArgumentNullException(nameof(convo));
            string serialized = convo.ToJson(ChatFilter.All);
            return _tokenizer.Count(serialized, _opts.TokenizerModel ?? model);
        }
    }
}
