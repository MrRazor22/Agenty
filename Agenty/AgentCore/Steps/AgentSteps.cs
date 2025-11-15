using Agenty.LLMCore;
using Agenty.LLMCore.ChatHandling;
using Agenty.LLMCore.JsonSchema;
using Agenty.LLMCore.Messages;
using Agenty.LLMCore.ToolHandling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Agenty.AgentCore.Steps
{
    public sealed class ErrorHandlingStep : IAgentStep
    {
        public async Task InvokeAsync(IAgentContext ctx, AgentStepDelegate next)
        {
            try
            {
                await next(ctx);
            }
            catch (Exception ex)
            {
                var logger = ctx.Services.GetService<ILogger<ErrorHandlingStep>>();
                logger?.LogError(ex, "Unhandled agent pipeline exception.");

                ctx.Chat.AddAssistant("An internal error occurred. Unable to continue.");
                ctx.Response.Set("Sorry, something went wrong while processing your request.");
            }
        }
    }

    public sealed class PlanningStep : IAgentStep
    {
        private readonly string? _model;
        private readonly ReasoningMode _mode;
        private readonly LLMCallOptions? _opts;

        public PlanningStep(string? model = null, ReasoningMode mode = ReasoningMode.Balanced, LLMCallOptions? opts = null)
        {
            _model = model;
            _mode = mode;
            _opts = opts;
        }
        public class Plan
        {
            public List<string> Steps { get; set; }

            public override string ToString()
            {
                if (Steps == null || Steps.Count == 0)
                    return string.Empty;

                return string.Join("\n",
                    Steps.Select((s, i) => $"Step {i + 1}: {s}"));
            }
        }

        public async Task InvokeAsync(IAgentContext ctx, AgentStepDelegate next)
        {
            var llm = ctx.Services.GetRequiredService<ILLMClient>();
            var tools = ctx.Services.GetRequiredService<IToolCatalog>();

            var sysPrompt = $@"Break the user request into a minimal ordered set of actionable steps.";

            var convo = new Conversation()
                .CloneFrom(ctx.Chat, ~ChatFilter.System)
                .AddSystem(sysPrompt);

            var plan = await llm.GetStructuredTyped<Plan>(convo, ToolCallMode.None, _mode, _model, _opts, ctx.CancellationToken);

            if (plan != null && plan.Steps.Count > 0)
            {
                ctx.Items["plan"] = plan;
                ctx.Chat.AddAssistant("Let's proceed with these steps:\n" + ctx.Items["plan"]);
            }

            await next(ctx);

            string finalResponse = "No valid respose generated. Try Again";
            if (plan != null && plan.Steps.Count > 0)
            {
                convo = new Conversation()
               .CloneFrom(ctx.Chat)
               .AddAssistant("Now I will provide a summary of all steps and results.");

                var res = await llm.GetResponseStreaming(convo, ToolCallMode.None, _mode, _model, _opts, ctx.CancellationToken);
                finalResponse = res.AssistantMessage ?? finalResponse;
            }
            ctx.Chat.AddAssistant(finalResponse);
            ctx.Response.Set(finalResponse);
        }
    }

    public sealed class StreamingToolCallingStep : IAgentStep
    {
        private readonly string? _model;
        private readonly ReasoningMode _mode;
        private readonly ToolCallMode _toolMode;
        private readonly int _maxIterations;
        private readonly LLMCallOptions? _opts;

        public StreamingToolCallingStep(
            string? model = null,
            ReasoningMode mode = ReasoningMode.Balanced,
            ToolCallMode toolMode = ToolCallMode.Auto,
            int maxIterations = 10,
            LLMCallOptions? opts = null)
        {
            _model = model;
            _mode = mode;
            _toolMode = toolMode;
            _maxIterations = maxIterations;
            _opts = opts;
        }

        public async Task InvokeAsync(IAgentContext ctx, AgentStepDelegate next)
        {
            var llm = ctx.Services.GetRequiredService<ILLMClient>();
            var runtime = ctx.Services.GetRequiredService<IToolRuntime>();

            int iteration = 0;

            while (iteration < _maxIterations && !ctx.CancellationToken.IsCancellationRequested)
            {
                // STREAM LIVE
                var result = await llm.GetResponseStreaming(
                    ctx.Chat,
                    _toolMode,
                    _mode,
                    _model,
                    _opts,
                    ctx.CancellationToken,
                    onChunk: chunk =>
                    {
                        if (chunk.Kind == StreamKind.Text)
                            ctx.Stream?.Invoke(chunk.AsText()!);
                    });

                // text
                var assistantText = result.AssistantMessage ?? "";
                ctx.Chat.AddAssistant(assistantText);

                // toolcall?
                var toolCall = result.Payload.FirstOrDefault(); // result.Payload is List<ToolCall>
                if (toolCall == null)
                {
                    ctx.Response.Set(assistantText);
                    break;
                }

                // RUN TOOL
                var outputs = await runtime.HandleToolCallsAsync(
                    new List<ToolCall> { toolCall },
                    ctx.CancellationToken);

                ctx.Chat.AppendToolCallAndResults(outputs);

                iteration++;
            }

            await next(ctx);
        }
    }
}
