using Agenty.LLMCore;
using Agenty.LLMCore.ChatHandling;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Agenty.AgentCore.Runtime
{
    public delegate Task AgentStepDelegate(IAgentContext ctx);

    public interface IAgentStep
    {
        Task InvokeAsync(IAgentContext ctx, AgentStepDelegate next);
    }

    // === Pipeline builder (C#7/.NET Standard 2.0 safe) ===
    public sealed class AgentPipelineBuilder
    {
        private readonly List<Func<AgentStepDelegate, AgentStepDelegate>> _components =
            new List<Func<AgentStepDelegate, AgentStepDelegate>>();

        public AgentPipelineBuilder Use(Func<IAgentContext, AgentStepDelegate, Task> middleware)
        {
            _components.Add(next => ctx => middleware(ctx, next));
            return this;
        }

        // parameterless ctor steps
        public AgentPipelineBuilder Use<TStep>() where TStep : IAgentStep, new()
        {
            _components.Add(next => ctx => new TStep().InvokeAsync(ctx, next));
            return this;
        }

        // ctor args steps (generic, no typeof/object[])
        public AgentPipelineBuilder Use<TStep>(Func<TStep> factory) where TStep : IAgentStep
        {
            _components.Add(next => ctx => factory().InvokeAsync(ctx, next));
            return this;
        }

        public AgentPipelineBuilder Run(Func<IAgentContext, Task> terminal)
        {
            _components.Add(_ => ctx => terminal(ctx));
            return this;
        }

        public AgentStepDelegate Build()
        {
            AgentStepDelegate app = ctx => Task.CompletedTask;
            for (int i = _components.Count - 1; i >= 0; i--)
                app = _components[i](app);
            return app;
        }
    }

}
