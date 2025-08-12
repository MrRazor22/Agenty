using Agenty.LLMCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Agenty.AgentCore
{
    public class PromptBuilder
    {
        AgentMemory memory;
        public PromptBuilder(AgentMemory memory)
        {
            this.memory = memory;
        }

        public Conversations StepExecute()
        {
            return new Conversations()
                .Add(Role.User, memory.Goal)
                .Add(Role.User, memory.Plan.ToString())
                .Add(Role.User, memory.Thoughts.ToString())
                .Add(Role.User, memory.Plan.GetCurrentStep());
        }

        public Conversations StepEvaluate()
        {
            return new Conversations()
                .Add(Role.User, memory.Goal)
                .Add(Role.User, memory.Plan.ToString())
                .Add(Role.User, memory.Thoughts.ToString());
        }
    }
}

