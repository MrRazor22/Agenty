using Agenty.Chat;
using Agenty.LLMCore;
using Agenty.Tools;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Agenty.AgentCore
{
    public interface IAgentExecutor
    {
        Task ExecuteAsync(IAgentContext ctx);
    }
    public class ToolCallingLoop : IAgentExecutor
    {
        private readonly string? _model;
        private readonly ReasoningMode _mode;
        private readonly ToolCallMode _toolMode;
        private readonly int _maxIterations;
        private readonly LLMSamplingOptions? _opts;

        public ToolCallingLoop(
            string? model = null,
            ReasoningMode mode = ReasoningMode.Balanced,
            ToolCallMode toolMode = ToolCallMode.OneTool,
            int maxIterations = 50,
            LLMSamplingOptions? opts = null)
        {
            _model = model;
            _mode = mode;
            _toolMode = toolMode;
            _maxIterations = maxIterations;
            _opts = opts;
        }

        public async Task ExecuteAsync(IAgentContext ctx)
        {
            ctx.ScratchPad.AddUser(ctx.UserRequest ?? "No User input.");

            var llm = ctx.Services.GetRequiredService<ILLMClient>();
            var runtime = ctx.Services.GetRequiredService<IToolRuntime>();

            int iteration = 0;

            while (iteration < _maxIterations && !ctx.CancellationToken.IsCancellationRequested)
            {
                // STREAM LIVE
                var result = await llm.GetResponseAsync(
                    ctx.ScratchPad,
                    _toolMode,
                    _model,
                    _mode,
                    _opts,
                    ctx.CancellationToken,
                    s => ctx.Stream?.Invoke(s));

                // text 
                ctx.ScratchPad.AddAssistant(result.AssistantMessage);

                // toolcall?
                var toolCall = result.ToolCalls.FirstOrDefault(); // result.Payload is List<ToolCall>
                if (toolCall == null)
                {
                    ctx.Response.Set(result.AssistantMessage);
                    break;
                }

                // RUN TOOL
                var outputs = await runtime.HandleToolCallsAsync(
                    new List<ToolCall> { toolCall },
                    ctx.CancellationToken);

                ctx.ScratchPad.AppendToolCallAndResults(outputs);

                iteration++;
            }
        }
    }
}
