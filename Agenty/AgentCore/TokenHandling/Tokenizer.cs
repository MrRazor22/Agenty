using SharpToken;

namespace Agenty.AgentCore.TokenHandling
{
    public interface ITokenizer
    {
        int CountTokens(string text);
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

        public int CountTokens(string text) =>
            _encoder.Encode(text).Count;

        public IReadOnlyList<int> Encode(string text) =>
            _encoder.Encode(text);

        public string Decode(IEnumerable<int> tokens) =>
            _encoder.Decode(tokens.ToList());
    }
}
