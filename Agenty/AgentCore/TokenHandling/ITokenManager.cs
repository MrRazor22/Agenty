using Agenty.LLMCore.ChatHandling;
using SharpToken;
using System.Threading;

namespace Agenty.AgentCore.TokenHandling
{

    public struct TokenUsage
    {
        public int InputTokens;
        public int OutputTokens;

        public TokenUsage(int input, int output)
        {
            InputTokens = input;
            OutputTokens = output;
        }

        public int Total { get { return InputTokens + OutputTokens; } }
        public static readonly TokenUsage Empty = new TokenUsage(0, 0);
    }

    public interface ITokenManager
    {
        void Record(int inputTokens, int outputTokens);
        TokenUsage GetTotals();
    }

    internal sealed class TokenManager : ITokenManager
    {
        private long _input;
        private long _output;

        public void Record(int inputTokens, int outputTokens)
        {
            Interlocked.Add(ref _input, inputTokens);
            Interlocked.Add(ref _output, outputTokens);
        }

        public TokenUsage GetTotals()
        {
            var input = Interlocked.Read(ref _input);
            var output = Interlocked.Read(ref _output);
            return new TokenUsage((int)input, (int)output);
        }
    }
}
