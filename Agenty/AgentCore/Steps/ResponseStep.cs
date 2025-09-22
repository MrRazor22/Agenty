using Agenty.AgentCore;
using Agenty.AgentCore.Steps;
using Agenty.LLMCore;
using Agenty.LLMCore.ChatHandling;

public sealed class ResponseStep : IAgentStep<object, string>
{
    private readonly string? _extraInstruction;
    private readonly Role _roleForInstruction;

    public ResponseStep(string? extraInstruction = null, Role roleForInstruction = Role.System)
    {
        _extraInstruction = extraInstruction;
        _roleForInstruction = roleForInstruction;
    }

    public async Task<string?> RunAsync(IAgentContext ctx, object? input = null)
    {
        var chat = ctx.Memory.Working;
        var llm = ctx.LLM;

        if (!string.IsNullOrWhiteSpace(_extraInstruction))
            chat.Add(_roleForInstruction, _extraInstruction);

        var resp = await llm.GetResponse(chat, LLMMode.Balanced);

        if (!string.IsNullOrWhiteSpace(resp))
            chat.Add(Role.Assistant, resp);

        return resp;
    }
}
