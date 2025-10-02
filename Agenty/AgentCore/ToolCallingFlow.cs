using Agenty.AgentCore.Runtime;
using Agenty.LLMCore;
using Agenty.LLMCore.ChatHandling;
using Agenty.LLMCore.ToolHandling;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Agenty.AgentCore
{
    public sealed class ToolCallingStep : IAgentStep
    {
        private readonly Tool[]? _restrictedTools;
        private readonly Func<IToolRegistry, IEnumerable<Tool>>? _toolFilter;
        private readonly ToolCallMode _toolCallMode;
        private readonly LLMMode _mode;

        public ToolCallingStep(
            Tool[]? restrictedTools = null,
            Func<IToolRegistry, IEnumerable<Tool>>? toolFilter = null,
            ToolCallMode toolCallMode = ToolCallMode.Auto,
            LLMMode mode = LLMMode.Creative)
        {
            _restrictedTools = restrictedTools;
            _toolFilter = toolFilter;
            _toolCallMode = toolCallMode;
            _mode = mode;
        }

        public async Task<string> RunAsync(IAgentContext ctx)
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

            return resp.AssistantMessage; // pipeline continues
        }
    }
}
