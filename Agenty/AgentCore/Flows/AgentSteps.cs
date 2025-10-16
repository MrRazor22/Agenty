using Agenty.AgentCore.Runtime;
using Agenty.LLMCore;
using Agenty.LLMCore.ChatHandling;
using Agenty.LLMCore.Messages;
using Agenty.LLMCore.ToolHandling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpToken;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Agenty.AgentCore.Flows
{
    public sealed class PlanningStep : IAgentStep
    {
        private readonly string? _model;
        private readonly ReasoningMode _mode;

        public PlanningStep(string? model = null, ReasoningMode mode = ReasoningMode.Balanced)
        {
            _model = model;
            _mode = mode;
        }

        public async Task InvokeAsync(IAgentContext ctx, AgentStepDelegate next)
        {
            var llm = ctx.Services.GetRequiredService<ILLMCoordinator>();
            var tools = ctx.Services.GetRequiredService<IToolCatalog>();

            var plan = await llm.GetResponse(new Conversation().CloneFrom(ctx.Chat)
                .AddSystem("Plan the steps to handle the user’s request using the available tools. Keep it short, clear, and action-focused.")
                .AddUser($"Plan for: {ctx.UserRequest}"),
                _mode, _model);

            if (!string.IsNullOrWhiteSpace(plan))
            {
                ctx.Items["plan"] = plan;
                ctx.Chat.AddAssistant($"Planned approach: {plan}");
            }

            await next(ctx);
        }
    }

    public sealed class ToolCallingStep : IAgentStep
    {
        private readonly string? _model;
        private readonly ReasoningMode _mode;
        int maxIteratios;
        int iterations = 0;
        public ToolCallingStep(string? model = null, ReasoningMode mode = ReasoningMode.Balanced, int maxIterations = 50)
        {
            _model = model;
            _mode = mode;
            maxIteratios = maxIterations;
        }

        public async Task InvokeAsync(IAgentContext ctx, AgentStepDelegate next)
        {
            var llm = ctx.Services.GetRequiredService<ILLMCoordinator>();

            var resp = await llm.GetToolCallResponse(ctx.Chat, ToolCallMode.Auto, _mode, _model);
            while (resp.Calls.Count > 0 && iterations < maxIteratios)
            {
                var results = await llm.RunToolCalls(resp.Calls.ToList());
                ctx.Chat.AppendToolCallAndResults(results);
                resp = await llm.GetToolCallResponse(ctx.Chat, ToolCallMode.Auto, _mode, _model);
                iterations++;
            }

            ctx.Chat.AddAssistant(resp.AssistantMessage!);

            await next(ctx);
        }
    }

    public sealed class FinalSummaryStep : IAgentStep
    {
        private readonly string? _model;
        private readonly ReasoningMode _mode;

        public FinalSummaryStep(string? model = null, ReasoningMode mode = ReasoningMode.Creative)
        {
            _model = model;
            _mode = mode;
        }

        public async Task InvokeAsync(IAgentContext ctx, AgentStepDelegate next)
        {
            var llm = ctx.Services.GetRequiredService<ILLMCoordinator>();

            var summaryChat = new Conversation()
                .CloneFrom(ctx.Chat, ChatFilter.All & ~ChatFilter.System)  // Keep the structured messages
                .AddSystem($"Summarize the conversation into one short, clear detailed answer to the user’s request.")
                .AddUser($"{ctx.UserRequest}");

            var response = await llm.GetResponse(
                summaryChat,
                _mode, _model);

            ctx.Response.Set(response);
            var logger = ctx.Services.GetService<ILogger<FinalSummaryStep>>();
            logger?.LogInformation($"Final summary: {ctx.Response.Message}");
            await next(ctx);
        }
    }

    public sealed class ReflectionStep : IAgentStep
    {
        private readonly string? _model;

        public ReflectionStep(string? model = null)
        {
            _model = model;
        }

        public async Task InvokeAsync(IAgentContext ctx, AgentStepDelegate next)
        {
            var llm = ctx.Services.GetRequiredService<ILLMCoordinator>();

            var response = ctx.Response.Message;

            if (!string.IsNullOrWhiteSpace(response))
            {
                var innerChat = new Conversation().CloneFrom(ctx.Chat);
                // Ask model to evaluate last answer via structured schema
                innerChat.AddUser($"Evaluate the last assistant answer for request [{ctx.UserRequest}] Return your verdict.");

                var verdict = await llm.GetStructured<Verdict>(innerChat, ReasoningMode.Deterministic, _model);

                if (verdict != null)
                {
                    if (verdict.confidence_score == confidence.no ||
                        verdict.confidence_score == confidence.maybe)
                    {
                        if (verdict.confidence_score == confidence.maybe &&
                            !string.IsNullOrWhiteSpace(verdict.explanation))
                        {
                            innerChat.AddAssistant("I think I may not have fully answered the user's request, because " + verdict.explanation + " I shall at least give a user-friendly answer mentioning what I accomplished and also addressing that.");

                            // let model refine further
                            var refinement = await llm.GetResponse(innerChat, ReasoningMode.Balanced, _model);
                            if (!string.IsNullOrWhiteSpace(refinement))
                                response ??= refinement;
                        }
                    } 
                    ctx.Chat.AddAssistant(response);
                    ctx.Response.Set(response);
                }
            }

            await next(ctx);
        }
        public enum confidence { no, yes, maybe }
        public sealed class Verdict
        {

            public confidence confidence_score { get; set; }
            public string explanation { get; set; }
        }
    }
}
