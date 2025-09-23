using Agenty.AgentCore.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Agenty.AgentCore.Steps.ControlFlow
{
    // MapStep: transforms one step’s output type into another
    public sealed class MapStep<TIn, TOut> : IAgentStep<TIn, TOut>
    {
        private readonly Func<TIn?, TOut?> _mapper;

        public MapStep(Func<TIn?, TOut?> mapper)
        {
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        }

        public Task<TOut?> RunAsync(IAgentContext ctx, TIn? input = default)
        {
            var result = _mapper(input);
            return Task.FromResult(result);
        }
    }
}
