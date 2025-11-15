using Agenty.AgentCore.Runtime;
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

namespace Agenty.AgentCore.Flows
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
            var llm = ctx.Services.GetRequiredService<ILLMCoordinator>();
            var tools = ctx.Services.GetRequiredService<IToolCatalog>();

            var sysPrompt = $@"Break the user request into a minimal ordered set of actionable steps.";

            var convo = new Conversation()
                .CloneFrom(ctx.Chat, ~ChatFilter.System)
                .AddSystem(sysPrompt);

            var plan = await llm.GetStructured<Plan>(convo, _mode, _model, _opts, ctx.CancellationToken);

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

                var res = await llm.GetResponse(convo, ToolCallMode.None, _mode, _model, _opts, ctx.CancellationToken);
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
            if (ctx.Stream == null)
                throw new InvalidOperationException("Streaming requires ctx.Stream.");

            var llm = ctx.Services.GetRequiredService<ILLMCoordinator>();
            var runtime = ctx.Services.GetRequiredService<IToolRuntime>();

            int iteration = 0;

            while (iteration < _maxIterations &&
                   !ctx.CancellationToken.IsCancellationRequested)
            {
                ctx.StreamBegin();

                // STREAM → TEXT + POSSIBLE TOOLCALL
                await foreach (var chunk in llm.StreamResponse(
                    ctx.Chat,
                    _toolMode,        // <-- DO NOT CHANGE THIS MID-LOOP
                    _mode,
                    _model,
                    _opts,
                    ctx.CancellationToken))
                {
                    ctx.StreamApply(chunk);
                }

                var assistantText = ctx.StreamFinalText();
                var toolCall = ctx.StreamFinalToolCall();

                if (!string.IsNullOrWhiteSpace(assistantText))
                    ctx.Chat.AddAssistant(assistantText);

                // If no toolcall → this is FINAL ANSWER
                if (toolCall == null)
                {
                    ctx.Response.Set(assistantText);
                    break;
                }

                // EXECUTE TOOL
                var results = await runtime.HandleToolCallsAsync(
                    new List<ToolCall> { toolCall },
                    ctx.CancellationToken);

                ctx.Chat.AppendToolCallAndResults(results);

                // LOOP AGAIN and stream next round
                iteration++;
            }

            await next(ctx);
        }

    }

}
