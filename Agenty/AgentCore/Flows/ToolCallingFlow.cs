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
    public sealed class ToolCallingStep : IAgentStep
    {
        private readonly ToolCallMode _toolCallMode;
        private readonly ReasoningMode _mode;
        private readonly System.Func<IToolRegistry, IEnumerable<Tool>>? _toolFilter;

        public ToolCallingStep(
            ToolCallMode toolCallMode,
            ReasoningMode mode,
            System.Func<IToolRegistry, IEnumerable<Tool>>? toolFilter = null)
        {
            _toolCallMode = toolCallMode;
            _mode = mode;
            _toolFilter = toolFilter;
        }

        public async Task InvokeAsync(IAgentContext ctx, AgentStepDelegate next)
        {
            var llm = ctx.Services.GetRequiredService<ILLMCoordinator>();
            var tools = _toolFilter?.Invoke(ctx.Tools).ToArray()
                        ?? ctx.Tools.RegisteredTools.ToArray();

            var resp = await llm.GetToolCallResponse(ctx.Chat, _toolCallMode, _mode, tools);
            while (resp.Calls.Count > 0)
            {
                var results = await llm.RunToolCalls(resp.Calls.ToList());
                ctx.Chat.AppendToolResults(results);

                resp = await llm.GetToolCallResponse(ctx.Chat, _toolCallMode, _mode, tools);
            }

            if (!string.IsNullOrWhiteSpace(resp.AssistantMessage))
                ctx.Chat.Add(Role.Assistant, resp.AssistantMessage);

            await next(ctx);
        }
    }

    public sealed class PlanningStep : IAgentStep
    {
        public async Task InvokeAsync(IAgentContext ctx, AgentStepDelegate next)
        {
            var llm = ctx.Services.GetRequiredService<ILLMCoordinator>();
            var plan = await llm.GetStructured<Plan>(ctx.Chat);

            if (plan != null)
            {
                ctx.Items["plan"] = plan;
                ctx.Services.GetService<ILogger<PlanningStep>>()?
                    .LogInformation("Generated plan: {@Plan}", plan);
            }

            await next(ctx);
        }

        public sealed class Plan
        {
            public List<string> Steps { get; set; } = new List<string>();
        }
    }

    public sealed class ReflectionStep : IAgentStep
    {
        public async Task InvokeAsync(IAgentContext ctx, AgentStepDelegate next)
        {
            var llm = ctx.Services.GetRequiredService<ILLMCoordinator>();

            var lastAssistant = ctx.Chat.LastOrDefault(m => m.Role == Role.Assistant);
            var lastMessage = (lastAssistant?.Content as TextContent)?.Text;

            if (!string.IsNullOrWhiteSpace(lastMessage))
            {
                // Ask model to evaluate last answer via structured schema
                ctx.Chat.Add(Role.User, $"Evaluate the last assistant answer for question [{ctx.Goal}] Return JSON verdict.");

                var verdict = await llm.GetStructured<Verdict>(ctx.Chat, ReasoningMode.Deterministic);

                if (verdict != null)
                {
                    ctx.Services.GetService<ILogger<ReflectionStep>>()?
                        .LogInformation("Reflection verdict: {@Verdict}", verdict);

                    if (verdict.confidence_score == Verdict.Confidence.Yes ||
                        verdict.confidence_score == Verdict.Confidence.Partial)
                    {
                        if (verdict.confidence_score == Verdict.Confidence.Partial &&
                            !string.IsNullOrWhiteSpace(verdict.explanation))
                        {
                            ctx.Chat.Add(Role.User, verdict.explanation);
                            // let model refine further
                            var refinement = await llm.GetResponse(ctx.Chat, ReasoningMode.Balanced);
                            if (!string.IsNullOrWhiteSpace(refinement))
                                ctx.Chat.Add(Role.Assistant, refinement);
                        }
                    }
                    else
                    {
                        ctx.Chat.Add(Role.User, (verdict.explanation ?? "Try again.") +
                            " If possible, correct this using the tools.");
                    }
                }
            }

            await next(ctx);
        }

        public sealed class Verdict
        {
            public enum Confidence { Unknown, No, Partial, Yes }
            public Confidence confidence_score { get; set; }
            public string explanation { get; set; }
        }
    }

    /// <summary>
    /// Final step — summarizes the entire chat and streams the assistant’s final message.
    /// </summary>
    public sealed class FinalSummaryStep : IAgentStep
    {
        public async Task InvokeAsync(IAgentContext ctx, AgentStepDelegate next)
        {
            var logger = ctx.Services.GetService<ILogger<FinalSummaryStep>>();
            var llmClient = ctx.Services.GetRequiredService<ILLMClient>();
            string summaryPrompt =
                "Summarize the entire conversation so far into a single clear, complete, and concise answer to the user's goal.";

            ctx.Chat.Add(Role.System, summaryPrompt);

            // Check if the client supports streaming
            var supportsStreaming = llmClient is OpenAILLMClient; // you can abstract this later

            if (supportsStreaming)
            {
                logger?.LogInformation("FinalSummaryStep: streaming response...");
                var buffer = "";

                await foreach (var chunk in llmClient.GetStreamingResponse(ctx.Chat, ReasoningMode.Balanced))
                {
                    Console.Write(chunk);
                    buffer += chunk;
                }

                Console.WriteLine(); // newline after stream end
                ctx.SetResult(buffer);
            }
            else
            {
                logger?.LogInformation("FinalSummaryStep: non-streaming fallback...");
                var coordinator = ctx.Services.GetRequiredService<ILLMCoordinator>();
                var response = await coordinator.GetResponse(ctx.Chat, ReasoningMode.Balanced);
                ctx.SetResult(response);
            }

            // Optional: continue pipeline
            if (next != null)
                await next(ctx);
        }
    }
}
