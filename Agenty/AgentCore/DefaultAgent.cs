using Agenty.LLMCore;
using Agenty.LLMCore.Providers.OpenAI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Agenty.AgentCore
{
    internal class DefaultAgent : IAgent
    {
        private ILLMClient _llm;
        private IToolCoordinator _toolCoordinator;
        private IToolRegistry _toolRegistry = new ToolRegistry();
        public static DefaultAgent Create() => new DefaultAgent();
        public DefaultAgent WithLLM(string baseUrl, string apiKey, string modelName)
        {
            _llm = new OpenAILLMClient();
            _llm.Initialize(baseUrl, apiKey, modelName);
            _toolCoordinator = new ToolCoordinator(_llm, _toolRegistry);
            return this;
        }

        public DefaultAgent WithTools<T>()
        {
            _toolRegistry.RegisterAll<T>();
            return this;
        }

        public async Task<string> ExecuteAsync(string goal, int maxRounds = 50)
        {
            throw new NotImplementedException();
        }
    }
}
