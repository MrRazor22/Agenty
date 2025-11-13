using Agenty.LLMCore.ChatHandling;
using Agenty.LLMCore.JsonSchema;
using Agenty.LLMCore.ToolHandling;
using Microsoft.Extensions.Options;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;
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
        Task<T?> ExecuteAsync<T>(
            Func<Conversation, Task<T?>> action,
            Conversation prompt)
            where T : class;
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

        public async Task<T?> ExecuteAsync<T>(
            Func<Conversation, Task<T?>> action,
            Conversation prompt)
            where T : class
        {
            if (!_options.Enabled)
                return await action(prompt);

            var intPrompt = new Conversation().CloneFrom(prompt);

            for (int attempt = 0; attempt <= _options.MaxRetries; attempt++)
            {
                try
                {
                    var task = action(intPrompt);
                    var completed = await Task.WhenAny(task, Task.Delay(_options.Timeout));
                    if (completed != task)
                        throw new TimeoutException($"LLM call timed out after {_options.Timeout.TotalSeconds}s");

                    return await task;
                }
                catch (Exception ex)
                {
                    if (attempt == _options.MaxRetries)
                        throw;

                    intPrompt.AddAssistant($"Attempt {attempt + 1} failed: {ex.Message}. Retrying...");
                    var delay = TimeSpan.FromMilliseconds(
                        _options.InitialDelay.TotalMilliseconds * Math.Pow(_options.BackoffFactor, attempt));
                    await Task.Delay(delay);
                }
            }

            return default;
        }
    }

}
