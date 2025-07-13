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
