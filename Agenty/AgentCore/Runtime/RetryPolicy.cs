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
        Task<T> ExecuteAsync<T>(
            Func<Conversation, Task<T>> action,
            Conversation prompt);
        IAsyncEnumerable<LLMStreamChunk> ExecuteStreamAsync(
        Func<IAsyncEnumerable<LLMStreamChunk>> streamFactory,
        CancellationToken ct = default);
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

        public async Task<T> ExecuteAsync<T>(
            Func<Conversation, Task<T>> action,
            Conversation prompt)
        {
            if (!_options.Enabled)
                return await action(prompt);

            var intPrompt = new Conversation().CloneFrom(prompt);

            for (int attempt = 0; attempt <= _options.MaxRetries; attempt++)
            {
                try
                {
                    var task = action(intPrompt);

                    var completed =
                        await Task.WhenAny(task, Task.Delay(_options.Timeout));

                    if (completed != task)
                        throw new TimeoutException(
                            $"LLM call timed out after {_options.Timeout.TotalSeconds}s");

                    return await task;
                }
                catch when (attempt < _options.MaxRetries)
                {
                    intPrompt.AddAssistant($"Retry {attempt + 1} due to error.");

                    var delay = TimeSpan.FromMilliseconds(
                        _options.InitialDelay.TotalMilliseconds *
                        Math.Pow(_options.BackoffFactor, attempt));

                    await Task.Delay(delay);
                }
            }

            return default!;
        }

        // --------------------------------------------------------
        // STREAMING VERSION
        // --------------------------------------------------------

        public async IAsyncEnumerable<LLMStreamChunk> ExecuteStreamAsync(
    Func<IAsyncEnumerable<LLMStreamChunk>> streamFactory,
    [EnumeratorCancellation] CancellationToken ct = default)
        {
            if (!_options.Enabled)
            {
                await foreach (var chunk in streamFactory().WithCancellation(ct))
                    yield return chunk;
                yield break;
            }

            int attempt = 0;

            while (attempt <= _options.MaxRetries)
            {
                var succeeded = true;

                using var timeoutCts = new CancellationTokenSource(_options.Timeout);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

                var stream = streamFactory(); // fresh enumerator for each attempt
                var enumerator = stream.GetAsyncEnumerator(linked.Token);

                try
                {
                    while (true)
                    {
                        bool moved;
                        try
                        {
                            moved = await enumerator.MoveNextAsync();
                        }
                        catch
                        {
                            succeeded = false;
                            break;
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
                    yield break; // stream finished cleanly

                attempt++;

                if (attempt > _options.MaxRetries)
                    yield break;

                // Emit a small retry marker as a Text chunk using the single-struct type
                yield return new LLMStreamChunk(
                    StreamKind.Text,
                    payload: $"[retry {attempt}]");

                var delay = TimeSpan.FromMilliseconds(
                    _options.InitialDelay.TotalMilliseconds *
                    Math.Pow(_options.BackoffFactor, attempt - 1));

                await Task.Delay(delay, ct);
            }
        }
    }
}
