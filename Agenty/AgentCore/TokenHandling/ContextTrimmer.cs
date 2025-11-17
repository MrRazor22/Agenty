using Agenty.LLMCore.ChatHandling;
using SharpToken;
using System;
using System.Linq;
using System.Reflection;

namespace Agenty.AgentCore.TokenHandling
{
    public interface ITokenizer
    {
        int Count(string text, string? model = null);
    }

    internal sealed class SharpTokenTokenizer : ITokenizer
    {
        private readonly string defaultModel;
        public SharpTokenTokenizer(string? model = null)
        {
            defaultModel = model ?? "cl100k_base";
        }
        public int Count(string text, string? model = null)
        {
            var encoder = !string.IsNullOrWhiteSpace(model)
                ? GptEncoding.GetEncodingForModel(model)
                : GptEncoding.GetEncoding(defaultModel);

            return encoder.Encode(text ?? string.Empty).Count;
        }
    }
    public sealed class ContextTrimOptions
    {
        public int MaxContextTokens { get; set; } = 8000;
        public double Margin { get; set; } = 0.8;
        public string? TokenizerModel { get; set; } = null;
    }

    public interface IContextTrimmer
    {
        void Trim(Conversation convo, int? maxTokens = null, string? model = null);
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

        public void Trim(Conversation convo, int? maxTokens = null, string? model = null)
        {
            if (convo == null) throw new ArgumentNullException(nameof(convo));

            // choose override or default from options
            int effectiveMax = maxTokens ?? _opts.MaxContextTokens;
            int limit = (int)(effectiveMax * _opts.Margin);

            // pick tokenizer model override
            var tokenizerModel = _opts.TokenizerModel ?? model;

            int count = Estimate(convo, tokenizerModel);

            if (count <= limit)
                return;

            // keep system messages
            var system = convo.Where(c => c.Role == Role.System).ToList();

            // remove tool noise
            convo.RemoveAll(c => c.Role == Role.Tool);

            count = Estimate(convo, tokenizerModel);
            if (count <= limit)
                return;

            // sliding window for U/A messages
            var core = convo.Where(c => c.Role == Role.User || c.Role == Role.Assistant).ToList();

            while (count > limit && core.Count > 1)
            {
                convo.Remove(core[0]);
                core.RemoveAt(0);
                count = Estimate(convo, tokenizerModel);
            }

            // restore system messages at top
            foreach (var s in system)
                if (!convo.Contains(s))
                    convo.Insert(0, s);
        }

        public int Estimate(Conversation convo, string? model = null)
        {
            if (convo == null) throw new ArgumentNullException(nameof(convo));
            string serialized = convo.ToJson(ChatFilter.All);
            return _tokenizer.Count(serialized, _opts.TokenizerModel ?? model);
        }
    }
}
