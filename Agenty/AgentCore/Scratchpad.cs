using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Agenty.AgentCore
{
    public class ScratchpadEntry
    {
        [Description("Description of the action taken, e.g. tool called with parameters, or reasoning step without a tool call")]
        public string ActionTaken { get; set; }

        [Description("Agent’s interpretation or notes based on tool result or reasoning outcome")]
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
    }

}
