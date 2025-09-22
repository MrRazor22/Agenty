using Agenty.AgentCore.Runtime;
using Agenty.LLMCore;
using Agenty.LLMCore.ChatHandling;
using Agenty.LLMCore.ToolHandling;

namespace Agenty.AgentCore.Steps
{
    /// <summary>
    /// Step that runs tool calls until none remain.
    /// Optionally restricts to a given set of tools.
    /// </summary>
    public sealed class ToolCallingStep : IAgentStep<object, object>
    {
        private readonly Tool[]? _tools;
        private readonly ToolCallMode _toolCallMode;
        private readonly LLMMode _mode;

        public ToolCallingStep(
            Tool[]? tools = null,
            ToolCallMode toolCallMode = ToolCallMode.Auto,
            LLMMode mode = LLMMode.Balanced)
        {
            _tools = tools;
            _toolCallMode = toolCallMode;
            _mode = mode;
        }

        public async Task<object?> RunAsync(
            Conversation chat, ILLMCoordinator llm, object? input = null)
        {
            if (llm == null)
                throw new InvalidOperationException("LLM is required for ToolCallingStep.");

            var resp = await llm.GetToolCallResponse(chat, _toolCallMode, _mode, _tools ?? Array.Empty<Tool>());

            while (resp.Calls.Count > 0)
            {
                chat.AppendToolResults(await llm.RunToolCalls(resp.Calls.ToList()));
                resp = await llm.GetToolCallResponse(chat, _toolCallMode, _mode, _tools ?? Array.Empty<Tool>());
            }

            // continue pipeline with same input
            return input;
        }
    }
}
