using Agenty.LLMCore.JsonSchema;
using Agenty.LLMCore.Messages;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Agenty.LLMCore.ToolHandling
{
    public interface IToolRuntime
    {
        Task<object?> InvokeAsync(ToolCall toolCall, CancellationToken ct = default);
        Task<IReadOnlyList<ToolCallResult>> HandleToolCallsAsync(IEnumerable<ToolCall> toolCalls, CancellationToken ct = default);
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
        {
            var argsKey = call.Arguments?.NormalizeArgs() ?? "";
            return $"{call.Name}:{argsKey}";
        }

        public async Task<object?> InvokeAsync(ToolCall toolCall, CancellationToken ct = default)
        {
            if (toolCall == null) throw new ArgumentNullException(nameof(toolCall));
            ct.ThrowIfCancellationRequested();

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
                    ct.ThrowIfCancellationRequested();
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
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new ToolExecutionException(
                    toolCall.Name,
                    $"Failed to invoke tool `{toolCall.Name}`: {ex.Message}",
                    ex);
            }
        }

        public async Task<IReadOnlyList<ToolCallResult>> HandleToolCallsAsync(IEnumerable<ToolCall> toolCalls, CancellationToken ct = default)
        {
            var results = new List<ToolCallResult>();
            var batchCache = new Dictionary<string, object?>();

            foreach (var call in toolCalls)
            {
                ct.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(call.Name) && !string.IsNullOrWhiteSpace(call.Message))
                {
                    results.Add(new ToolCallResult(call, null));
                    continue;
                }

                var key = MakeKey(call);
                if (batchCache.ContainsKey(key))
                {
                    _logger?.LogWarning("Duplicate call detected for {Tool}; ignored.", call.Name);
                    continue;
                }

                try
                {
                    var result = await InvokeAsync(call, ct).ConfigureAwait(false);
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
                catch (OperationCanceledException)
                {
                    break;
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
