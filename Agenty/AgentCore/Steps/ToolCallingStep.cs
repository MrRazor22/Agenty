using Agenty.AgentCore.Runtime;
using Agenty.LLMCore;
using Agenty.LLMCore.ChatHandling;
using Agenty.LLMCore.ToolHandling;

namespace Agenty.AgentCore.Steps
{
    /// <summary>
    /// Step that runs tool calls until none remain.
    /// Uses all tools from the registry unless restricted or filtered.
    /// </summary>
    public sealed class ToolCallingStep : IAgentStep<object, object>
    {
        private readonly Tool[]? _restrictedTools;
        private readonly Func<IToolRegistry, IEnumerable<Tool>>? _toolFilter;
        private readonly ToolCallMode _toolCallMode;
        private readonly LLMMode _mode;

        public ToolCallingStep(
            Tool[]? restrictedTools = null,
            Func<IToolRegistry, IEnumerable<Tool>>? toolFilter = null,
            ToolCallMode toolCallMode = ToolCallMode.Auto,
            LLMMode mode = LLMMode.Balanced)
        {
            _restrictedTools = restrictedTools;
            _toolFilter = toolFilter;
            _toolCallMode = toolCallMode;
            _mode = mode;
        }

        public async Task<object?> RunAsync(IAgentContext ctx, object? input = null)
        {
            var chat = ctx.Memory.Working;
            var llm = ctx.LLM;

            if (llm == null)
                throw new InvalidOperationException("LLM is required for ToolCallingStep.");

            // Select tools
            var tools = _restrictedTools
                ?? _toolFilter?.Invoke(ctx.Tools).ToArray()
                ?? ctx.Tools.RegisteredTools.ToArray();

            var resp = await llm.GetToolCallResponse(chat, _toolCallMode, _mode, tools);

            while (resp.Calls.Count > 0)
            {
                var results = await llm.RunToolCalls(resp.Calls.ToList());
                chat.AppendToolResults(results);

                resp = await llm.GetToolCallResponse(chat, _toolCallMode, _mode, tools);
            }

            return input; // pipeline continues
        }
    }
}
