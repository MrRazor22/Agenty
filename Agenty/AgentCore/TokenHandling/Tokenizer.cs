using SharpToken;
using System.Collections.Generic;
using System.Linq;

namespace Agenty.AgentCore.TokenHandling
{
    public interface ITokenizer
    {
        IReadOnlyList<int> Encode(string text);
        string Decode(IEnumerable<int> tokens);
    }

    public sealed class SharpTokenTokenizer : ITokenizer
    {
        private readonly GptEncoding _encoder;

        // Explicit ctor lets caller choose
        public SharpTokenTokenizer(string model)
        {
            _encoder = GptEncoding.GetEncodingForModel(model);
        }

        // Default ctor falls back to cl100k_base (used by GPT-3.5/4)
        public SharpTokenTokenizer()
        {
            _encoder = GptEncoding.GetEncoding("cl100k_base");
        }

        public IReadOnlyList<int> Encode(string text) =>
            _encoder.Encode(text ?? string.Empty);

        public string Decode(IEnumerable<int> tokens) =>
            _encoder.Decode(tokens?.ToList() ?? new List<int>());
    }
}
