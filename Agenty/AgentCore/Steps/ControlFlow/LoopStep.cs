using Agenty.AgentCore.Runtime;
using Agenty.LLMCore.ChatHandling;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Agenty.AgentCore.Steps.ControlFlow
{
    // === LoopStep: cache pipeline once, reuse across iterations ===
    public sealed class LoopStep : IAgentStep<object, object>
    {
        private readonly StepExecutor _body;
        private readonly Func<object?, bool> _breakCondition;
        private readonly int _maxRounds;

        public LoopStep(
            Action<StepExecutor.Builder> body,
            Func<object?, bool>? breakCondition = null,
            int maxRounds = 10)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));

            var builder = new StepExecutor.Builder();
            body(builder);
            _body = builder.Build(); // ✅ build once, cache

            _breakCondition = breakCondition ?? (r => r is string s && !string.IsNullOrWhiteSpace(s));
            _maxRounds = maxRounds;
        }

        public async Task<object?> RunAsync(IAgentContext ctx, object? input = null)
        {
            object? lastResult = input;

            for (int round = 0; round < _maxRounds; round++)
            {
                ctx.Logger?.LogDebug("LoopStep iteration {Round}/{MaxRounds} started", round + 1, _maxRounds);

                lastResult = await _body.RunAsync(ctx, lastResult);

                if (_breakCondition(lastResult))
                {
                    ctx.Logger?.LogDebug("LoopStep break condition met at round {Round}", round + 1);
                    return lastResult;
                }
            }

            ctx.Logger?.LogWarning("LoopStep max rounds ({MaxRounds}) reached. Returning last result.", _maxRounds);
            ctx.Memory.Working.Add(Role.System, $"[LoopStep] Max rounds ({_maxRounds}) reached, returning last result.");
            return lastResult;
        }
    }
}
