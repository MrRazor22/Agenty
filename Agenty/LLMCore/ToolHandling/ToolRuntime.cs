using Agenty.LLMCore.Messages;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Agenty.LLMCore.ToolHandling
{
    public interface IToolRuntime
    {
        Task<object?> InvokeAsync(ToolCall toolCall);

        /// <summary>
        /// Run tool calls. Always returns results. 
        /// </summary>
        Task<IReadOnlyList<ToolCallResult>> HandleToolCallsAsync(IEnumerable<ToolCall> toolCalls);
    }

    public sealed class ToolRuntime : IToolRuntime
    {
        private readonly IToolRegistry _registry;

        public ToolRuntime(IToolRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public async Task<object?> InvokeAsync(ToolCall toolCall)
        {
            if (toolCall == null) throw new ArgumentNullException(nameof(toolCall));

            var tool = _registry.Get(toolCall.Name);
            if (tool?.Function == null)
                throw new ToolExecutionException(
                    toolCall.Name,
                    $"Tool '{toolCall.Name}' not registered or has no function.",
                    new InvalidOperationException()
                );

            try
            {
                var func = tool.Function;
                var method = func.Method;
                var returnType = method.ReturnType;

                if (typeof(Task).IsAssignableFrom(returnType))
                {
                    var task = (Task)func.DynamicInvoke(toolCall.Parameters)!;
                    await task.ConfigureAwait(false);

                    if (returnType.IsGenericType &&
                        returnType.GetGenericTypeDefinition() == typeof(Task<>))
                    {
                        var resultProperty = returnType.GetProperty("Result")!;
                        return resultProperty.GetValue(task);
                    }

                    return null;
                }
                else
                {
                    return func.DynamicInvoke(toolCall.Parameters);
                }
            }
            catch (Exception ex)
            {
                throw new ToolExecutionException(
                    toolCall.Name,
                    $"Failed to invoke tool `{toolCall.Name}`: {ex.Message}",
                    ex
                );
            }
        }

        public async Task<IReadOnlyList<ToolCallResult>> HandleToolCallsAsync(
            IEnumerable<ToolCall> toolCalls)
        {
            var results = new List<ToolCallResult>();

            foreach (var call in toolCalls)
            {
                if (string.IsNullOrWhiteSpace(call.Name) && !string.IsNullOrWhiteSpace(call.Message))
                {
                    results.Add(new ToolCallResult(call, null, null));
                    continue;
                }

                try
                {
                    var result = await InvokeAsync(call);
                    results.Add(new ToolCallResult(call, result, null));
                }
                catch (ToolValidationAggregateException vex)
                {
                    // Structured validation failure
                    results.Add(new ToolCallResult(call, null, vex));
                }
                catch (ToolExecutionException tex)
                {
                    results.Add(new ToolCallResult(call, null, tex));
                }
            }

            return results;
        }

    }

    // Custom exception type for clarity
    public sealed class ToolExecutionException : Exception
    {
        public string ToolName { get; }
        public ToolExecutionException(string toolName, string message, Exception inner)
            : base(message, inner) => ToolName = toolName;
    }
}
