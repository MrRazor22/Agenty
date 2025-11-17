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

                ctx.ScratchPad.AddAssistant("An internal error occurred. Unable to continue.");
                ctx.Response.Set("Sorry, something went wrong while processing your request.");
            }
        }
    }

    public sealed class PlanningStep : IAgentStep
    {
        private readonly string? _model;
        private readonly ReasoningMode _mode;
        private readonly LLMSamplingOptions? _opts;

        public PlanningStep(string? model = null, ReasoningMode mode = ReasoningMode.Balanced)
        {
            _model = model;
            _mode = mode;
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

            var planPrompt = @"Break my request into actionable steps based on the tools you got and information you know. If you dont know or have the information required ask the user DO NOT fabricate anything be honest about your limitations";

            var convo = new Conversation()
                .AddUser(ctx.UserRequest)
                .AddUser(planPrompt);

            var plan = await llm.GetStructuredAsync<Plan>(
                convo,
                model: _model,
                ct: ctx.CancellationToken,
                onStream: s => ctx.Stream?.Invoke(s));

            if (plan != null && plan.Steps.Count > 0)
            {
                ctx.Items["plan"] = plan;
                ctx.ScratchPad.AddUser("For my request please follow these steps:\n" + ctx.Items["plan"]);
            }

            await next(ctx);

            string finalResponse = "No valid respose generated. Try Again";
            if (plan != null && plan.Steps.Count > 0)
            {
                convo = new Conversation()
               .Clone(ctx.ScratchPad)
               .AddUser($"With all the available info you gathered for my request: '{ctx.UserRequest}', provide a final user facing answer.");

                var res = await llm.GetResponseAsync(
                    convo,
                    _model,
                    _mode,
                    _opts,
                    ctx.CancellationToken,
                    onStream: s => ctx.Stream?.Invoke(s));

                finalResponse = res.AssistantMessage ?? finalResponse;
            }
            ctx.ScratchPad.AddAssistant(finalResponse);
            ctx.Response.Set(finalResponse);
        }
    }

    public sealed class ToolCallingStep : IAgentStep
    {
        private readonly string? _model;
        private readonly ReasoningMode _mode;
        private readonly ToolCallMode _toolMode;
        private readonly int _maxIterations;
        private readonly LLMSamplingOptions? _opts;

        public ToolCallingStep(
            string? model = null,
            ReasoningMode mode = ReasoningMode.Balanced,
            ToolCallMode toolMode = ToolCallMode.Auto,
            int maxIterations = 10,
            LLMSamplingOptions? opts = null)
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

            await next(ctx);
        }
    }
}
