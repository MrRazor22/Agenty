using System;
using System.Collections.Generic;

namespace Agenty.AgentCore.TokenHandling
{
    public readonly struct TokenUsage
    {
        public int InputTokens { get; }
        public int OutputTokens { get; }
        public int Total => InputTokens + OutputTokens;

        public TokenUsage(int input, int output)
        {
            InputTokens = input;
            OutputTokens = output;
        }

        public static TokenUsage operator -(TokenUsage a, TokenUsage b)
            => new TokenUsage(a.InputTokens - b.InputTokens, a.OutputTokens - b.OutputTokens);

        public static readonly TokenUsage Empty = new TokenUsage(0, 0);
    }

    public interface ITokenManager
    {
        /// <summary>
        /// Record token usage. If source is null, uses current step from StepContext.
        /// </summary>
        void Record(int inputTokens, int outputTokens, string? source = null);

        /// <summary>
        /// Get total tokens used across all calls.
        /// </summary>
        TokenUsage GetTotals();

        /// <summary>
        /// Get token usage broken down by source (step name, etc).
        /// </summary>
        IReadOnlyDictionary<string, TokenUsage> GetBySource();
    }

    public sealed class TokenManager : ITokenManager
    {
        private int _input;
        private int _output;
        private readonly Dictionary<string, TokenUsage> _sources = new Dictionary<string, TokenUsage>();
        private readonly object _lock = new object();

        public void Record(int inputTokens, int outputTokens, string? source = null)
        {
            lock (_lock)
            {
                // Always update global totals
                _input += inputTokens;
                _output += outputTokens;

                // Determine source: explicit > AsyncLocal > "Unknown"
                var actualSource = source
                    ?? StepContext.Current.Value
                    ?? "Unknown";

                // Track by source
                var usage = _sources.TryGetValue(actualSource, out var existing)
                    ? existing
                    : TokenUsage.Empty;

                usage = new TokenUsage(
                    usage.InputTokens + inputTokens,
                    usage.OutputTokens + outputTokens);

                _sources[actualSource] = usage;
            }
        }

        public TokenUsage GetTotals() => new TokenUsage(_input, _output);

        public IReadOnlyDictionary<string, TokenUsage> GetBySource() => _sources;
    }
}