using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Agenty.AgentCore
{
    public class FeedBack
    {
        [Description("True if the progress aligns with the overall goal.")]
        public bool IsAlignedWithGoal { get; set; }

        [Description("Concise explanation summarizing whether progress is on track or drifting.")]
        public string Reasoning { get; set; } = "";

    }

}
