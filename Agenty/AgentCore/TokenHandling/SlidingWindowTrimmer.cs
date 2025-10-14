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

    public interface IContextTrimmer
    {
        void Trim(Conversation convo, int? maxTokens = null, string? model = null);
        int Estimate(Conversation convo, string? model = null);
    }

    internal sealed class SlidingWindowTrimmer : IContextTrimmer
    {
        private readonly ITokenizer _tokenizer;
        private readonly int _defaultMaxTokens;
        private readonly double _margin;

        public SlidingWindowTrimmer(ITokenizer tokenizer = null, int defaultMaxTokens = 8000, double margin = 1.0)
        {
            _tokenizer = tokenizer ?? new SharpTokenTokenizer();
            _defaultMaxTokens = defaultMaxTokens > 0 ? defaultMaxTokens : 8000;
            _margin = margin <= 0 || margin > 1 ? 1.0 : margin;
        }

        public void Trim(Conversation convo, int? maxTokens = null, string? model = null)
        {
            if (convo == null) throw new ArgumentNullException(nameof(convo));

            int limit = (int)((maxTokens ?? _defaultMaxTokens) * _margin);
            int count = Estimate(convo, model);
            if (count <= limit) return;

            var system = convo.Where(c => c.Role == Role.System).ToList();
            convo.RemoveAll(c => c.Role == Role.Tool);

            count = Estimate(convo, model);
            if (count <= limit) return;

            var core = convo.Where(c => c.Role == Role.User || c.Role == Role.Assistant).ToList();
            while (count > limit && core.Count > 1)
            {
                convo.Remove(core.First());
                core.RemoveAt(0);
                count = Estimate(convo, model);
            }

            foreach (var s in system)
                if (!convo.Contains(s))
                    convo.Insert(0, s);
        }

        public int Estimate(Conversation convo, string? model = null)
        {
            if (convo == null) throw new ArgumentNullException(nameof(convo));
            string serialized = convo.ToJson(ChatFilter.All);
            return _tokenizer.Count(serialized, model);
        }
    }
}
