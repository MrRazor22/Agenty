using Agenty.LLMCore;
using Agenty.LLMCore.ChatHandling;
using Agenty.LLMCore.JsonSchema;
using Agenty.LLMCore.ToolHandling;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Agenty.AgentCore.Runtime
{
    public sealed class RetryPolicyOptions
    {
        public int MaxRetries { get; set; } = 3;
        public TimeSpan InitialDelay { get; set; } = TimeSpan.FromMilliseconds(500);
        public double BackoffFactor { get; set; } = 2.0;
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(120);
        public bool Enabled { get; set; } = true;
    }

    public interface IRetryPolicy
    {
        IAsyncEnumerable<LLMStreamChunk> ExecuteStreamAsync(
            LLMRequestBase originalRequest,
            Func<LLMRequestBase, IAsyncEnumerable<LLMStreamChunk>> factory,
            [EnumeratorCancellation] CancellationToken ct = default);
    }

    /// <summary>
    /// Default policy, preserves existing retry loops (JSON + Tool calls).
    /// </summary>
    public sealed class DefaultRetryPolicy : IRetryPolicy
    {
        private readonly RetryPolicyOptions _options;

        public DefaultRetryPolicy(IOptions<RetryPolicyOptions>? options = null)
        {
            _options = options?.Value ?? new RetryPolicyOptions();
        }

        public async IAsyncEnumerable<LLMStreamChunk> ExecuteStreamAsync(
            LLMRequestBase originalRequest,
            Func<LLMRequestBase, IAsyncEnumerable<LLMStreamChunk>> factory,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            for (int attempt = 0; attempt <= _options.MaxRetries; attempt++)
            {
                var clonedRequest = originalRequest.DeepClone();

                bool succeeded = true;

                using var timeoutCts = new CancellationTokenSource(_options.Timeout);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

                var stream = factory(clonedRequest);
                var enumerator = stream.GetAsyncEnumerator(linked.Token);

                try
                {
                    while (true)
                    {
                        bool moved;

                        try { moved = await enumerator.MoveNextAsync(); }
                        catch { succeeded = false; break; }

                        if (!moved) break;

                        yield return enumerator.Current;
                    }
                }
                finally
                {
                    await enumerator.DisposeAsync();
                }

                if (succeeded)
                    yield break;

                if (attempt == _options.MaxRetries)
                    yield break;

                clonedRequest.Prompt.AddAssistant($"Retry {attempt + 1} due to error.");

                yield return new LLMStreamChunk(StreamKind.Text, $"[retry {attempt + 1}]");

                await Task.Delay(
                    TimeSpan.FromMilliseconds(_options.InitialDelay.TotalMilliseconds *
                                              Math.Pow(_options.BackoffFactor, attempt)),
                    ct);
            }
        }
    }
}
