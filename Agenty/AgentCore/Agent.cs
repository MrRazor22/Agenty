using Agenty.AgentCore.Runtime;
using Agenty.LLMCore.ChatHandling;
using Agenty.LLMCore.ToolHandling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;


namespace Agenty.AgentCore
{
    /// <summary>
    /// Execution context for an agent run.
    /// </summary>
    public interface IAgentContext
    {
        // Agent state
        IToolRegistry Tools { get; }
        Conversation Chat { get; }
        string? Goal { get; set; } // must be settable per run

        // Result of the run (always present)
        IAgentResult Result { get; }

        // Extensibility / DI
        IServiceProvider Services { get; }

        // Scratchpad
        IDictionary<string, object?> Items { get; }

        // Cancellation for long-running steps
        CancellationToken CancellationToken { get; }

        // Controlled mutation of the result
        void SetResult(string? message = null, object? payload = null);
    }

    /// <summary>
    /// Output of an agent run.
    /// </summary>
    public interface IAgentResult
    {
        string? Message { get; }
        object? Payload { get; }
    }

    internal sealed class AgentResult : IAgentResult
    {
        public string? Message { get; private set; }
        public object? Payload { get; private set; }

        internal void Update(string? message, object? payload)
        {
            Message = message;
            Payload = payload;
        }
    }

    internal sealed class AgentContext : IAgentContext
    {
        private readonly AgentResult _result = new AgentResult();

        public AgentContext(IServiceProvider services, CancellationToken cancellationToken = default)
        {
            Services = services;
            CancellationToken = cancellationToken;
        }

        public IToolRegistry Tools { get; } = new ToolRegistry();
        public Conversation Chat { get; } = new Conversation();
        public string? Goal { get; set; }

        public IAgentResult Result => _result;
        public IServiceProvider Services { get; }
        public IDictionary<string, object?> Items { get; } = new Dictionary<string, object?>();
        public CancellationToken CancellationToken { get; }

        public void SetResult(string? message = null, object? payload = null)
            => _result.Update(message, payload);
    }


    public interface IAgent
    {
        IAgentContext Context { get; }
        Task<IAgentResult> ExecuteAsync(string goal);
    }

    public sealed class Agent : IAgent
    {
        private readonly AgentContext _ctx;
        private AgentStepDelegate? _rootPipeline;

        public IAgentContext Context => _ctx;

        private Agent(IServiceProvider provider)
        {
            _ctx = new AgentContext(provider);
        }

        public static Agent Create(IServiceCollection services)
        {
            var provider = services.BuildServiceProvider();
            return new Agent(provider);
        }

        // === Fluent config ===
        public Agent WithTools<T>(params string[] tags)
        {
            _ctx.Tools.RegisterAll<T>(tags);
            return this;
        }

        public Agent WithTools<T>(T instance, params string[] tags)
        {
            _ctx.Tools.RegisterAll<T>(instance, tags);
            return this;
        }

        public Agent WithSystemPrompt(string prompt)
        {
            _ctx.Chat.Add(Role.System, prompt);
            return this;
        }

        // CHANGED: takes delegate not IAgentStep
        public Agent WithFlow(AgentStepDelegate pipeline)
        {
            _rootPipeline = pipeline;
            return this;
        }

        // === Run ===
        public async Task<IAgentResult> ExecuteAsync(string goal)
        {
            if (_rootPipeline == null)
                throw new InvalidOperationException("Pipeline not set. Call WithFlow().");

            // set the execution goal in context
            _ctx.Goal = goal;
            _ctx.Chat.Add(Role.User, goal);

            var logger = _ctx.Services.GetService<ILogger<Agent>>();
            logger?.LogInformation("Starting agent run with goal: {Goal}", goal);

            await _rootPipeline(_ctx);  // just invoke the delegate

            logger?.LogInformation("Agent run complete");

            return _ctx.Result;
        }
    }

}
