using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Agenty.AgentCore
{
    public enum StepAction
    {
        Call,
        Finish,
        AskUser
    }

    public class PlanStep
    {
        public string NextStep { get; set; }
        public string? Tool { get; set; }
    }


    public class FeedBack
    {
        [Description("True if the overall goal accomplished.")]
        public bool IsGoalAccomplished { get; set; }

        [Description("Concise explanation summarizing the understanding from current step executed")]
        public string Understanding { get; set; } = "";

    }

    public class ScratchpadEntry
    {
        [Description("Description of the action taken, e.g. tool called with parameters, or reasoning step without a tool call")]
        public string ActionTaken { get; set; }

        [Description("interpretation or notes based on tool result or reasoning outcome")]
        public string Insights { get; set; }
    }


    public class Scratchpad
    {
        private readonly List<ScratchpadEntry> _entries = new();
        public IReadOnlyList<ScratchpadEntry> Entries => _entries;

        public void AddEntry(ScratchpadEntry entry) => _entries.Add(entry);
        public void RemoveEntriesAfterStep(int stepIndex)
        {
            if (stepIndex < 0 || stepIndex >= _entries.Count) return;
            _entries.RemoveRange(stepIndex, _entries.Count - stepIndex);
        }
        public void Clear() => _entries.Clear();

        public override string ToString()
        {
            if (!_entries.Any()) return "";
            var sb = new StringBuilder();
            int idx = 1;
            foreach (var entry in _entries)
            {
                sb.Append($"Step {idx} - Action Taken: {entry.ActionTaken}. Insights: {entry.Insights}. ");
                idx++;
            }
            return sb.ToString().Trim();
        }
    }
}
