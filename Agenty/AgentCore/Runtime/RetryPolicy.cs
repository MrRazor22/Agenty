using Agenty.LLMCore.ChatHandling;
using Agenty.LLMCore.JsonSchema;
using Agenty.LLMCore.ToolHandling;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Agenty.AgentCore.Runtime
{
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
        private readonly int _maxRetries;
        private readonly TimeSpan _initialDelay;
        private readonly double _backoffFactor;
        private readonly TimeSpan _timeout;

        public DefaultRetryPolicy(
            int maxRetries = 3,
            TimeSpan? initialDelay = null,
            double backoffFactor = 2.0,
            TimeSpan? timeout = null)
        {
            _maxRetries = maxRetries;
            _initialDelay = initialDelay ?? TimeSpan.FromMilliseconds(500);
            _backoffFactor = backoffFactor;
            _timeout = timeout ?? TimeSpan.FromSeconds(30);
        }

        public async Task<T?> ExecuteAsync<T>(
            Func<Conversation, Task<T?>> action,
            Conversation prompt)
            where T : class
        {
            var intPrompt = new Conversation().CloneFrom(prompt);

            for (int attempt = 0; attempt <= _maxRetries; attempt++)
            {
                try
                {
                    var task = action(intPrompt);
                    var completed = await Task.WhenAny(task, Task.Delay(_timeout));
                    if (completed != task)
                        throw new TimeoutException($"LLM call timed out after {_timeout.TotalSeconds}s");

                    return await task;
                }
                catch (Exception ex)
                {
                    if (attempt == _maxRetries)
                        throw;

                    intPrompt.AddAssistant($"Attempt {attempt + 1} failed: {ex.Message}. Retrying...");
                    var delay = TimeSpan.FromMilliseconds(
                        _initialDelay.TotalMilliseconds * Math.Pow(_backoffFactor, attempt));
                    await Task.Delay(delay);
                }
            }

            return default;
        }
    }

}
