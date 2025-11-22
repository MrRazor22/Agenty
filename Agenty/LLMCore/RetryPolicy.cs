using Agenty.ChatHandling;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Agenty.LLMCore
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
    public class RetryException : Exception
    {
        public RetryException(string message) : base(message) { }
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
            var workingRequest = originalRequest.DeepClone();

            for (int attempt = 0; attempt <= _options.MaxRetries; attempt++)
            {
                bool succeeded = true;
                string? errorMessage = null;

                using var timeoutCts = new CancellationTokenSource(_options.Timeout);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

                var clonedRequest = workingRequest.DeepClone();
                var stream = factory(clonedRequest);
                var enumerator = stream.GetAsyncEnumerator(linked.Token);

                try
                {
                    while (true)
                    {
                        bool moved;

                        try { moved = await enumerator.MoveNextAsync(); }
                        catch (RetryException retryEx)
                        {
                            succeeded = false;
                            errorMessage = retryEx.Message;
                            break;
                        }
                        catch (Exception ex)
                        {
                            throw; // real errors
                        }

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

                // Use the exception message to guide the model
                var retryMessage = $"Retry {attempt + 1} because: {errorMessage ?? "an error occurred"}";
                workingRequest.Prompt.AddAssistant(retryMessage);

                yield return new LLMStreamChunk(StreamKind.Text, $"[retry {attempt + 1}]: {retryMessage}");

                await Task.Delay(
                    TimeSpan.FromMilliseconds(_options.InitialDelay.TotalMilliseconds *
                                              Math.Pow(_options.BackoffFactor, attempt)),
                    ct);
            }
        }
    }
}
