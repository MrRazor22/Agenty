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

        public SharpTokenTokenizer(string model)
        {
            _encoder = GptEncoding.GetEncodingForModel(model);
        }

        public IReadOnlyList<int> Encode(string text) =>
            _encoder.Encode(text);

        public string Decode(IEnumerable<int> tokens) =>
            _encoder.Decode(tokens.ToList());
    }
}
