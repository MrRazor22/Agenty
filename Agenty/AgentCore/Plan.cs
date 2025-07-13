using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Agenty.AgentCore
{
    public class Plan : IPlan
    {
        private readonly List<PlanStep> _steps;

        public Plan(IEnumerable<string> steps)
        {
            _steps = steps.Select(desc => new PlanStep { Description = desc }).ToList();
        }
        public int CurrentStepIndex { get; set; } = 0;
        public IReadOnlyList<PlanStep> Steps => _steps;

        public int IndexOf(PlanStep step) => _steps.IndexOf(step);

        public void MarkComplete(int index, string insight)
        {
            if (index < 0 || index >= _steps.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            _steps[index].IsCompleted = true;
            _steps[index].Insight = insight;
        }
    }

}
