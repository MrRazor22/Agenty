using Agenty.LLMCore.Messages;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Agenty.LLMCore.ToolHandling
{
    public interface IToolRuntime
    {
        Task<object?> InvokeAsync(ToolCall toolCall);
        Task<IReadOnlyList<ToolCallResult>> HandleToolCallsAsync(IEnumerable<ToolCall> toolCalls);
    }

    public sealed class ToolRuntime : IToolRuntime
    {
        private readonly IToolCatalog _tools;
        private readonly ILogger<ToolRuntime>? _logger;

        public ToolRuntime(IToolCatalog registry, ILogger<ToolRuntime>? logger = null)
        {
            _tools = registry ?? throw new ArgumentNullException(nameof(registry));
            _logger = logger;
        }

        private static string MakeKey(ToolCall call)
            => $"{call.Name}:{JsonConvert.SerializeObject(call.Arguments)}";

        public async Task<object?> InvokeAsync(ToolCall toolCall)
        {
            if (toolCall == null) throw new ArgumentNullException(nameof(toolCall));

            var tool = _tools.Get(toolCall.Name);
            if (tool?.Function == null)
                throw new ToolExecutionException(
                    toolCall.Name,
                    $"Tool '{toolCall.Name}' not registered or has no function.",
                    new InvalidOperationException());

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
                return func.DynamicInvoke(toolCall.Parameters);
            }
            catch (Exception ex)
            {
                throw new ToolExecutionException(
                    toolCall.Name,
                    $"Failed to invoke tool `{toolCall.Name}`: {ex.Message}",
                    ex);
            }
        }

        public async Task<IReadOnlyList<ToolCallResult>> HandleToolCallsAsync(IEnumerable<ToolCall> toolCalls)
        {
            var results = new List<ToolCallResult>();
            var batchCache = new Dictionary<string, object?>();

            foreach (var call in toolCalls)
            {
                if (string.IsNullOrWhiteSpace(call.Name) && !string.IsNullOrWhiteSpace(call.Message))
                {
                    results.Add(new ToolCallResult(call, null));
                    continue;
                }

                var key = MakeKey(call);
                if (batchCache.ContainsKey(key))
                {
                    _logger?.LogWarning("Duplicate call detected for {Tool}; ignored.", call.Name);
                    continue; // don't return anything to LLM
                }

                try
                {
                    var result = await InvokeAsync(call);
                    batchCache[key] = result;
                    results.Add(new ToolCallResult(call, result));
                }
                catch (ToolValidationAggregateException vex)
                {
                    results.Add(new ToolCallResult(call, vex));
                }
                catch (ToolExecutionException tex)
                {
                    results.Add(new ToolCallResult(call, tex));
                }
            }

            return results;
        }
    }

    public sealed class ToolExecutionException : Exception
    {
        public string ToolName { get; }

        public ToolExecutionException(string toolName, string message, Exception inner)
            : base(message, inner) => ToolName = toolName;

        public override string ToString() => $"Tool '{ToolName}' failed. Reason: '{Message}'";
    }
}
