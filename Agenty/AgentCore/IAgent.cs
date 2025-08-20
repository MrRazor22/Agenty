using Agenty.LLMCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Agenty.AgentCore
{
    public interface IAgent
    {
        Task<string> ExecuteAsync(string goal);
    }


}
