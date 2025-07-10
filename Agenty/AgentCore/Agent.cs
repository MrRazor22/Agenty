using Agenty.LLMCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Agenty.AgentCore
{
    public class Agent(string Name = "Agent") : IAgent
    {
        public string Name { get; private set; } = Name;

        public IAgentMemory AgentMemory { get; private set; } = new AgentMemory(new Prompt(), new Scratchpad());
        public IPlan? CurrentPlan { get; private set; } = new Plan();
        public ILLMClient LLMClient { get; private set; } = null!;
        public IToolRegistry ToolRegistry { get; } = new ToolRegistry();
        public IToolExecutor ToolExecutor { get; private set; } = null!;
        public IAgentExecutor AgentExecutor { get; private set; } = null!;

        public Func<ToolCallInfo, string>? OnBeforeToolInvoke { get; set; }
        public Action<string>? OnThoughtGenerated { get; set; }
        public Action<string>? OnFinalResponseReady { get; set; }

        public IAgent WithModel(string baseUrl, string apiKey, string modelName = "any_model")
        {
            LLMClient = new OpenAIClient();
            LLMClient.Initialize(baseUrl, apiKey, modelName);
            return this;
        }
        public IAgent WithGoal(string goal)
        {
            AgentMemory.ChatHistory.Add(ChatRole.System, goal);
            return this;
        }

        public IAgent WithTools(List<Delegate> tools)
        {
            ToolRegistry.RegisterAll(tools);
            return this;
        }

        public IAgent WithMemory(IAgentMemory memory)
        {
            AgentMemory = memory;
            return this;
        }

        public IAgent WithExecutor(IAgentExecutor executor)
        {
            AgentExecutor = executor;
            return this;
        }

        public IAgent WithPlan(IPlan? plan)
        {
            CurrentPlan = plan;
            return this;
        }

        public Task<string> Execute(string userInput)
        {
            if (LLMClient == null) throw new InvalidOperationException("Model details not set");
            if (AgentExecutor == null) AgentExecutor = new Executor();
            return AgentExecutor.Execute(this, userInput);
        }
    }

}
