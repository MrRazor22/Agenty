using System.Text.Json;
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
            for (int i = 0; i < methodParams.Length; i++)
            {
                var p = methodParams[i];
                if (!argsObj.TryGetPropertyValue(p.Name!, out var node) || node == null)
                    paramValues[i] = Type.Missing;
                else
                    paramValues[i] = JsonSerializer.Deserialize(
                        node.ToJsonString(),
                        p.ParameterType,
                        new JsonSerializerOptions
                        {
                            Converters = { new JsonStringEnumConverter() }
                        });
            }

            var result = func.DynamicInvoke(paramValues);
            return result?.ToString();
        }
    }

}
