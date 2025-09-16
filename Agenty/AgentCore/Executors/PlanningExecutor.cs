using Agenty.LLMCore;
using Agenty.LLMCore.ChatHandling;
using Agenty.LLMCore.JsonSchema;
using Agenty.LLMCore.Logging;
using Agenty.LLMCore.ToolHandling;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Agenty.AgentCore.Executors
{
    public sealed class PlanningExecutor : IExecutor
    {
        private const string _systemPrompt =
            "You are an AI agent. " +
            "First, reason and create a short plan of steps. " +
            "Then execute step by step, using tools if needed. " +
            "After each step, reflect if the goal is satisfied. " +
            "Keep answers short, user-friendly.";

        public async Task<string> ExecuteAsync(IAgentContext context, string goal)
        {
            if (context.LLM == null)
                throw new InvalidOperationException("LLM not configured in AgentContext.");

            var llm = context.LLM;
            var coord = context.Tools;

            var chat = new Conversation().Append(context.Conversation);
            context.Logger?.AttachTo(chat);
            chat.Add(Role.System, _systemPrompt);
            chat.Add(Role.User, $"Goal: {goal}");

            const int maxRounds = 30;

            for (int round = 0; round < maxRounds; round++)
            {
                // Step 1: Ask model to propose or refine a plan
                var planResp = await llm.GetResponse(
                    chat.Add(Role.User, "Make or update your plan. Keep it numbered."),
                    LLMMode.Planning);

                chat.Add(Role.Assistant, planResp);

                // Step 2: Ask model for the next action
                var actionResp = await coord.GetToolCalls(
                    chat.Add(Role.User, "Execute next step in your plan. Use tools if needed."));

                // Step 3: Execute tool chaining
                await ExecuteToolChaining(coord, actionResp, chat);

                // Step 4: Check if satisfied
                var verdict = await llm.GetResponse(
                    chat.Add(Role.User, $"Is the goal '{goal}' completed? Answer yes/no and explain."),
                    LLMMode.Deterministic);

                if (verdict.Contains("yes", StringComparison.OrdinalIgnoreCase))
                {
                    var final = await llm.GetResponse(
                        chat.Add(Role.User, "Now give a final short answer for the user."),
                        LLMMode.Creative);

                    return final;
                }
            }

            // Fallback
            return await llm.GetResponse(
                chat.Add(Role.User, $"Time exceeded. Still answer: {goal}"),
                LLMMode.Creative);
        }

        private async Task ExecuteToolChaining(ILLMCoordinator coord, LLMResponse response, Conversation chat)
        {
            while (response.ToolCalls.Count != 0)
            {
                await HandleToolCalls(coord, response.ToolCalls, chat);
                response = await coord.GetToolCalls(chat);
            }
        }

        private async Task HandleToolCalls(ILLMCoordinator coord, List<ToolCall> toolCalls, Conversation chat)
        {
            foreach (var call in toolCalls)
            {
                chat.Add(Role.Assistant, null, toolCall: call);
                try
                {
                    var result = await coord.Invoke(call);
                    chat.Add(Role.Tool, ((object?)result).AsJSONString(), toolCall: call);
                }
                catch (Exception ex)
                {
                    chat.Add(Role.Tool, $"Tool error: {ex.Message}", toolCall: call);
                }
            }
        }
    }
}
