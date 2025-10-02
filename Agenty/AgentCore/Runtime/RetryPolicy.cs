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

        public DefaultRetryPolicy(
            int maxRetries = 3,
            TimeSpan? initialDelay = null,
            double backoffFactor = 2.0)
        {
            _maxRetries = maxRetries;
            _initialDelay = initialDelay ?? TimeSpan.FromMilliseconds(500);
            _backoffFactor = backoffFactor;
        }

        public async Task<T?> ExecuteAsync<T>(
            Func<Conversation, Task<T?>> action,
            Conversation prompt)
            where T : class
        {
            var intPrompt = Conversation.Clone(prompt);

            for (int attempt = 0; attempt <= _maxRetries; attempt++)
            {
                try
                {
                    return await action(intPrompt);
                }
                catch (Exception ex)
                {
                    if (attempt == _maxRetries)
                        throw;

                    intPrompt.Add(Role.Assistant,
                        $"The last response failed with [{ex.Message}]. Please retry.");

                    var delay = TimeSpan.FromMilliseconds(
                        _initialDelay.TotalMilliseconds * Math.Pow(_backoffFactor, attempt));

                    await Task.Delay(delay);
                }

                intPrompt.Add(Role.Assistant, "Invalid or empty output. Please try again.");
            }

            return default;
        }
    }

}
