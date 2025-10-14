using Agenty.AgentCore.Runtime;
using Agenty.LLMCore;
using Agenty.LLMCore.ChatHandling;
using Agenty.LLMCore.Messages;
using Agenty.LLMCore.Providers.OpenAI;
using Agenty.LLMCore.ToolHandling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Agenty.AgentCore.Flows
{
    public sealed class PlanningStep : IAgentStep
    {
        public async Task InvokeAsync(IAgentContext ctx, AgentStepDelegate next)
        {
            var llm = ctx.Services.GetRequiredService<ILLMCoordinator>();
            var tools = ctx.Services.GetRequiredService<IToolCatalog>();

            var plan = await llm.GetResponse(new Conversation().CloneFrom(ctx.Chat)
                .AddSystem("You are a planning assistant, Give a short brief concise plan for the given request in to executable tasks" +
                $"Assume that you can use these tools for the request: [{tools.RegisteredTools}].")
                .AddUser($"Give me a plan for the request [{ctx.Goal}]."));

            if (plan != null)
            {
                ctx.Items["plan"] = plan;
                ctx.Chat.AddAssistant("Here is what I'm gonna do to respond to user's request: " + plan);
            }

            await next(ctx);
        }
    }
    public sealed class ToolCallingStep : IAgentStep
    {
        public async Task InvokeAsync(IAgentContext ctx, AgentStepDelegate next)
        {
            var llm = ctx.Services.GetRequiredService<ILLMCoordinator>();

            var resp = await llm.GetToolCallResponse(ctx.Chat);
            while (resp.Calls.Count > 0)
            {
                var results = await llm.RunToolCalls(resp.Calls.ToList());
                ctx.Chat.AppendToolResults(results);

                resp = await llm.GetToolCallResponse(ctx.Chat);
            }

            if (!string.IsNullOrWhiteSpace(resp.AssistantMessage))
                ctx.Chat.AddAssistant(resp.AssistantMessage!);

            await next(ctx);
        }
    }
    public sealed class FinalSummaryStep : IAgentStep
    {
        public async Task InvokeAsync(IAgentContext ctx, AgentStepDelegate next)
        {
            var llm = ctx.Services.GetRequiredService<ILLMCoordinator>();

            var response = await llm.GetResponse(new Conversation().CloneFrom(ctx.Chat)
                .AddSystem("Summarize the entire conversation so far into a single clear, complete, and concise answer to the user's request.")
                .AddUser($"Here is the user's request [{ctx.Goal}]."),
                ReasoningMode.Creative);

            ctx.Result.Set(response, null);

            // Optional: continue pipeline
            if (next != null)
                await next(ctx);
        }
    }

    public sealed class ReflectionStep : IAgentStep
    {
        public async Task InvokeAsync(IAgentContext ctx, AgentStepDelegate next)
        {
            var llm = ctx.Services.GetRequiredService<ILLMCoordinator>();

            var response = ctx.Result.Message;

            if (!string.IsNullOrWhiteSpace(response))
            {
                var innerChat = new Conversation().CloneFrom(ctx.Chat);
                // Ask model to evaluate last answer via structured schema
                innerChat.Add(Role.User, $"Evaluate the last assistant answer for request [{ctx.Goal}] Return your verdict.");

                var verdict = await llm.GetStructured<Verdict>(ctx.Chat, ReasoningMode.Deterministic);

                if (verdict != null)
                {
                    if (verdict.confidence_score == confidence.no ||
                        verdict.confidence_score == confidence.maybe)
                    {
                        if (verdict.confidence_score == confidence.maybe &&
                            !string.IsNullOrWhiteSpace(verdict.explanation))
                        {
                            innerChat.AddAssistant("I think I may not have fully answered the user's goal, because " + verdict.explanation + " I shall at least give a user-friendly answer mentioning what I accomplished and also addressing that.");

                            // let model refine further
                            var refinement = await llm.GetResponse(innerChat, ReasoningMode.Balanced);
                            if (!string.IsNullOrWhiteSpace(refinement))
                                response = refinement;
                        }
                    }

                    ctx.Chat.Add(Role.Assistant, response);
                    ctx.Result.Set(response, null);
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
