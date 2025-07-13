using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Agenty.AgentCore
{
    public class Scratchpad : IScratchpad
    {
        private readonly List<string> _entries = new();
        private readonly Func<Action<string>?> _onUpdate;
        public IReadOnlyList<string> Entries => _entries;

        public Scratchpad(Func<Action<string>?> onUpdate)
        {
            _onUpdate = onUpdate;
        }

        public void Add(string entry)
        {
            _entries.Add(entry);
            _onUpdate()?.Invoke(entry);
        }

        public void ReplaceLast(string entry)
        {
            if (_entries.Count > 0)
                _entries[^1] = entry;
        }

        public bool RemoveLast()
        {
            if (_entries.Count == 0) return false;
            _entries.RemoveAt(_entries.Count - 1);
            return true;
        }

        public void Clear() => _entries.Clear();
    }

}
