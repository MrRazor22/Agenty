using Agenty.AgentCore.Runtime;
using Agenty.LLMCore;
using Agenty.LLMCore.ChatHandling;
using Agenty.LLMCore.Messages;
using Agenty.LLMCore.ToolHandling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SharpToken;
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
                // --- Code before next(ctx) (INWARD/REQUEST PATH) ---
                // Pass control to the rest of the agent pipeline (Planning, Tools, etc.)
                await next(ctx);
            }
            catch (Exception ex)
            {
                // --- Code after next(ctx) (OUTWARD/REVERSE/RESPONSE PATH) ---
                // This code runs only if an exception is thrown by a subsequent step 
                // and bubbles up the chain.

                var logger = ctx.Services.GetService<ILogger<ErrorHandlingStep>>();
                logger?.LogError(ex, "Agent pipeline failed due to an exception.");

                // 1. Clear the internal state (meaningful action)
                ctx.Chat.AddAssistant($"An unexpected error occurred: {ex.Message}. I cannot complete your request.");

                // 2. Set a clean, user-facing error response (meaningful action)
                // This prevents an empty or corrupt response from being returned.
                ctx.Response.Set("I'm sorry, I encountered a critical error while processing your request. Please try again or rephrase your goal.");
            }
        }
    }
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
            var toolCatalog = ctx.Services.GetRequiredService<IToolCatalog>();

            // Build a system prompt with role + instructions
            var sysPrompt = $@"
You are the *planner* component of an AI agent.  
Your task: Break down the user's request into **a clear, ordered list of minimal-steps**.  
Each step should reference the **available tools** when relevant, and be phrased as an actionable instruction (but you will *not* execute anything right now).

Available tools: {string.Join(", ", toolCatalog.RegisteredTools.Select(t => t.Name))}  
Constraints:  
- Use as few steps as needed.  
- Do not repeat the user request.  
- Do not execute the steps, only plan them.  
- If the request is ambiguous, include one clarifying question step.";

            var convo = new Conversation()
                .CloneFrom(ctx.Chat)
                .AddSystem(sysPrompt)
                .AddUser($"User request: {ctx.UserRequest}");

            var plan = await llm.GetResponse(convo, _mode, _model);

            if (!string.IsNullOrWhiteSpace(plan))
            {
                ctx.Items["plan"] = plan;
                ctx.Chat.AddSystem($"Planned approach:\n{plan}");
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
                ctx.Chat.AddAssistant(resp.AssistantMessage);
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

            // Feed the whole chat (minus system/meta junk) and tell the model to cleanly conclude it
            var polishChat = new Conversation()
                .CloneFrom(ctx.Chat, ChatFilter.All & ~ChatFilter.System)
                .AddSystem($@"
Give a single final response that directly answers the user's request.
Respond naturally and clearly to the user in a user friendy way.
")
                .AddUser(@"user's request: ""{ctx.UserRequest}""");

            var finalAnswer = await llm.GetResponse(polishChat, _mode, _model);

            ctx.Chat.AddAssistant(finalAnswer);
            ctx.Response.Set(finalAnswer);

            await next(ctx);
        }
    }

}
