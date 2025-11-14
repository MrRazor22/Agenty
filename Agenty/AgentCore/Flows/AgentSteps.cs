using Agenty.AgentCore.Runtime;
using Agenty.LLMCore;
using Agenty.LLMCore.ChatHandling;
using Agenty.LLMCore.JsonSchema;
using Agenty.LLMCore.ToolHandling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
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
        }
    }

    public sealed class ToolCallingStep : IAgentStep
    {
        private readonly string? _model;
        private readonly ReasoningMode _mode;
        private readonly ToolCallMode _toolMode;
        private readonly int _maxIterations;
        private readonly LLMCallOptions? _opts;

        public ToolCallingStep(
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
            var llm = ctx.Services.GetRequiredService<ILLMCoordinator>();
            var iterations = 0;

            var resp = await llm.GetToolCallResponse(
                ctx.Chat,
                _toolMode,
                _mode,
                _model,
                _opts,
                ctx.CancellationToken);

            while (!ctx.CancellationToken.IsCancellationRequested &&
                   resp.Calls.Count > 0 &&
                   iterations < _maxIterations)
            {
                if (!string.IsNullOrWhiteSpace(resp.AssistantMessage))
                    ctx.Chat.AddAssistant(resp.AssistantMessage);

                var results = await llm.RunToolCalls(resp.Calls.ToList(), ctx.CancellationToken);
                ctx.Chat.AppendToolCallAndResults(results);

                resp = await llm.GetToolCallResponse(
                    ctx.Chat,
                    _toolMode,
                    _mode,
                    _model,
                    _opts,
                    ctx.CancellationToken);

                iterations++;
            }

            if (!string.IsNullOrWhiteSpace(resp.AssistantMessage))
            {
                ctx.Chat.AddAssistant(resp.AssistantMessage);
                ctx.Response.Set(resp.AssistantMessage);
            }

            await next(ctx);
        }
    }
}