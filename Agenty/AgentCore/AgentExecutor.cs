using Agenty.LLMCore;
namespace Agenty.AgentCore;

public class Executor : IAgentExecutor
{
    public async Task<string> Execute(IAgent agent, string input)
    {
        if (agent.LLMClient is not { } llm)
            throw new InvalidOperationException("Agent has no LLM client set.");

        var tools = agent.ToolRegistry;
        var toolExecutor = agent.ToolExecutor ?? new ToolExecutor(tools);
        var memory = agent.AgentMemory;
        var prompt = memory.ChatHistory;
        var scratch = memory.Scratchpad;

        prompt.Add(ChatRole.User, input);
        scratch.Add($"[{Now()}] User: {input}");

        if (agent.CurrentPlan == null || agent.CurrentPlan.Steps.Count == 0)
        {
            var planner = new Planner(llm);
            var plan = await planner.GeneratePlan(memory, input);
            if (plan == null) scratch.Add("[No Plan Created");
            agent.WithPlan(plan);
            scratch.Add($"[{Now()}] Plan created with {plan.Steps.Count} steps.");
        }

        while (true)
        {
            var step = agent.CurrentPlan?.Steps.FirstOrDefault(s => !s.IsCompleted);
            if (step == null) break;

            scratch.Add($"[{Now()}] Step: {step.Description}");

            // Initial thought about the step
            var stepThought = await llm.GetResponse(prompt);
            if (!string.IsNullOrWhiteSpace(stepThought))
            {
                prompt.Add(ChatRole.Assistant, stepThought);
                scratch.Add($"[{Now()}] Thought: {stepThought}");
                agent.OnThoughtGenerated?.Invoke(stepThought);
            }

            // Check for tool usage
            var toolCall = (await llm.GetFunctionCallResponse(prompt, tools.GetRegisteredTools()))
                .FirstOrDefault(tc => !string.IsNullOrWhiteSpace(tc.Name));

            if (toolCall != null)
            {
                scratch.Add($"[{Now()}] Tool requested: {toolCall.Name}");

                var pre = agent.OnBeforeToolInvoke?.Invoke(toolCall);
                if (!string.IsNullOrWhiteSpace(pre))
                {
                    prompt.Add(ChatRole.Assistant, pre);
                    scratch.Add($"[{Now()}] Pre-tool: {pre}");
                }

                var toolResult = toolExecutor.InvokeTool(toolCall);
                prompt.Add(ChatRole.Tool, toolResult ?? "[null]", toolCall);
                scratch.Add($"[{Now()}] Tool result: {toolResult}");

                // Now let the model reflect on the tool result
                var postReflection = await llm.GetResponse(prompt);
                if (!string.IsNullOrWhiteSpace(postReflection))
                {
                    prompt.Add(ChatRole.Assistant, postReflection);
                    scratch.Add($"[{Now()}] Post-tool thought: {postReflection}");
                    agent.OnThoughtGenerated?.Invoke(postReflection);
                }

                agent.CurrentPlan.MarkComplete(agent.CurrentPlan.IndexOf(step), $"Used {toolCall.Name}: {toolResult}");
                continue;
            }

            // No tool used, just mark thought as result
            agent.CurrentPlan.MarkComplete(agent.CurrentPlan.IndexOf(step), $"LLM: {stepThought}");
            scratch.Add($"[{Now()}] Step complete without tool.");
        }

        prompt.Add(ChatRole.User, "Now give your final response without repeating or summarizing. No recap.");
        scratch.Add($"[{Now()}] Prompted model to finalize response.");

        var final = await llm.GetResponse(prompt);
        if (!string.IsNullOrWhiteSpace(final))
        {
            prompt.Add(ChatRole.Assistant, final);
            scratch.Add($"[{Now()}] Final: {final}");
            agent.OnFinalResponseReady?.Invoke(final);
            return final;
        }

        return "[No final response]";
    }

    private static string Now() => DateTime.UtcNow.ToString("HH:mm:ss");
}
