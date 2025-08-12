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

        public Conversation StepExecute()
        {
            return new Conversation()
                .Add(Role.User, memory.Goal)
                .Add(Role.User, memory.Plan.ToString())
                .Add(Role.User, memory.Thoughts.ToString())
                .Add(Role.User, memory.Plan.GetCurrentStep());
        }

        public Conversation StepEvaluate()
        {
            return new Conversation()
                .Add(Role.User, memory.Goal)
                .Add(Role.User, memory.Plan.ToString())
                .Add(Role.User, memory.Thoughts.ToString());
        }
    }
}

