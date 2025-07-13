using Agenty.Utils;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Agenty.LLMCore
{
    public class ToolExecutor(IToolRegistry registry) : IToolExecutor
    {
        public string? InvokeTool(ToolCallInfo toolCall)
        {
            if (toolCall == null) throw new ArgumentNullException("ToolCallInfo is null");

            var tool = registry.GetRegisteredTools()
                .FirstOrDefault(t => t.Name.Equals(toolCall.Name, StringComparison.OrdinalIgnoreCase));

            if (tool == null || tool.Function == null)
                return $"Tool '{toolCall.Name}' not registered or has no function.";

            var func = tool.Function;
            var method = func.Method;
            var methodParams = method.GetParameters();

            var argsObj = toolCall.Parameters;
            if (argsObj == null)
                return "[Invalid tool call parameter JSON]";

            var paramValues = new object?[methodParams.Length];
            if (methodParams.Length == 1 &&
            !Util.IsSimpleType(methodParams[0].ParameterType) &&
            argsObj is JsonObject rawRoot &&
            !rawRoot.ContainsKey(methodParams[0].Name!))
            {
                // Wrap it manually under the param name
                argsObj = new JsonObject
                {
                    [methodParams[0].Name!] = rawRoot
                };
            }

            for (int i = 0; i < methodParams.Length; i++)
            {
                var p = methodParams[i];
                if (!argsObj.TryGetPropertyValue(p.Name!, out var node) || node == null)
                    paramValues[i] = Type.Missing;
                else
                {
                    try
                    {
                        paramValues[i] = JsonSerializer.Deserialize(
                            node.ToJsonString(),
                            p.ParameterType,
                            new JsonSerializerOptions
                            {
                                Converters = { new JsonStringEnumConverter() }
                            });
                    }
                    catch
                    {
                        if (node is JsonValue val)
                        {
                            object? fallback = TryCoerceValue(val, p.ParameterType);
                            if (fallback != null)
                                paramValues[i] = fallback;
                            else
                                throw new JsonException($"Failed to coerce '{val}' to {p.ParameterType.Name} for '{p.Name}'");
                        }
                        else throw;
                    }
                }
            }

            var result = func.DynamicInvoke(paramValues);
            return result?.ToString();
        }

        private static object? TryCoerceValue(JsonValue val, Type targetType)
        {
            targetType = Nullable.GetUnderlyingType(targetType) ?? targetType;

            if (val.TryGetValue(out string str))
            {
                try
                {
                    if (targetType == typeof(long)) return long.Parse(str);
                    if (targetType == typeof(int)) return int.Parse(str);
                    if (targetType == typeof(bool)) return bool.Parse(str);
                    if (targetType == typeof(float)) return float.Parse(str);
                    if (targetType == typeof(double)) return double.Parse(str);
                }
                catch { }
            }

            return null;
        }

    }

}
