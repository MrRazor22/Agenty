using Agenty.LLMCore;
using Agenty.LLMCore.Providers.OpenAI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Agenty.AgentCore
{
    public class Agent : IAgent
    {
        private string _goal = "";
        private Plan _currentPlan = new();
        private readonly Scratchpad _scratchpad = new();
        private FeedBack? _criticFeedback;

        private int stepsSinceLastCritique = 0;
        private int critiqueInterval = 3;
        private int lastGoodStepIndex = 0;

        private ILLMClient _llm;
        private ToolCoordinator _toolCoordinator;
        private IToolRegistry _toolRegistry = new ToolRegistry();

        private Agent() { }
        public static Agent Create() => new Agent();
        public Agent WithLLM(string baseUrl, string apiKey, string modelName = "any_model")
        {
            _llm = new OpenAILLMClient();
            _llm.Initialize(baseUrl, apiKey, modelName);
            _toolCoordinator = new ToolCoordinator(_llm, _toolRegistry);
            return this;
        }

        public Agent WithTools<T>()
        {
            _toolRegistry.RegisterAll<T>();
            return this;
        }

        public Agent WithTools(params Delegate[] tools)
        {
            _toolRegistry.Register(tools);
            return this;
        }

        public Agent WithComponents(int critiqueInterval = 3)
        {
            this._toolRegistry.RegisterAll<AgentTools>("agent");
            this.critiqueInterval = critiqueInterval;
            return this;
        }

        public async Task<string> ExecuteAsync(string goal)
        {
            var toolsForMessage = _toolRegistry
                .GetByTags(false, "agent")
                .Select(t => t.ToString()); // uses your Tool.ToString()

            var toolsText = string.Join(", ", toolsForMessage);
            _goal = goal;
            var chat = new Conversation()
                .Add(Role.System, "You are a helpfull assistant")
                .Add(Role.User, $"Returns the immediate next step to execute for a given task: [{_goal}] with the tools [{toolsText}]");
            PlanStep step = await _toolCoordinator.GetStructuredResponse<PlanStep>(chat, AgentTools.GetNextStep);

            return step.Description + " | " + step.ToolName;
        }

    }
}
