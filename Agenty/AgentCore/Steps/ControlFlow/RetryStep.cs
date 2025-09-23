using Agenty.AgentCore.Runtime;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace Agenty.AgentCore.Steps.ControlFlow
{
    // === RetryStep: wraps another step with retries + backoff ===
    public sealed class RetryStep<TIn, TOut> : IAgentStep<TIn, TOut>
    {
        private readonly IAgentStep<TIn, TOut> _inner;
        private readonly int _maxRetries;
        private readonly TimeSpan _baseDelay;
        private readonly double _backoffFactor;

        public RetryStep(
            IAgentStep<TIn, TOut> inner,
            int maxRetries = 3,
            TimeSpan? baseDelay = null,
            double backoffFactor = 2.0)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _maxRetries = maxRetries;
            _baseDelay = baseDelay ?? TimeSpan.FromMilliseconds(200);
            _backoffFactor = backoffFactor;
        }

        public async Task<TOut?> RunAsync(IAgentContext ctx, TIn? input = default)
        {
            Exception? lastEx = null;
            var delay = _baseDelay;

            for (int attempt = 1; attempt <= _maxRetries; attempt++)
            {
                try
                {
                    ctx.Logger?.LogDebug("RetryStep attempt {Attempt}/{MaxRetries}", attempt, _maxRetries);
                    return await _inner.RunAsync(ctx, input);
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                    ctx.Logger?.LogWarning(ex, "RetryStep failed on attempt {Attempt}/{MaxRetries}", attempt, _maxRetries);
                    if (attempt < _maxRetries)
                        await Task.Delay(delay);
                    delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * _backoffFactor);
                }
            }

            ctx.Logger?.LogError(lastEx, "RetryStep exhausted all retries");
            throw lastEx ?? new Exception("RetryStep failed with no exception?");
        }
    }
}
