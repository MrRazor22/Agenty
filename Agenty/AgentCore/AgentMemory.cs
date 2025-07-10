using Agenty.LLMCore;

namespace Agenty.AgentCore
{
    public class Scratchpad : IScratchpad
    {
        private readonly List<string> _entries = new();
        public IReadOnlyList<string> Entries => _entries;

        public void Add(string entry) => _entries.Add(entry);
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


    public class Prompt : IPrompt
    {
        private readonly List<ChatInput> _messages = new();
        public IEnumerable<ChatInput> Messages => _messages;

        public void Add(ChatRole role, string content, ToolCallInfo? toolCallInfo = null)
            => _messages.Add(new ChatInput(role, content, toolCallInfo));

        public void AddMany(params (ChatRole role, string content)[] messages)
        {
            foreach (var (role, content) in messages)
                Add(role, content);
        }

        public void AddMany(params (ChatRole role, string content, ToolCallInfo? toolCallInfo)[] messages)
        {
            throw new NotImplementedException();
        }

        public void Clear() => _messages.Clear();

        public bool RemoveLast(ChatRole? role = null)
        {
            if (_messages.Count == 0) return false;
            _messages.RemoveAt(_messages.Count - 1);
            return true;
        }

        public bool RemoveMessage(Predicate<ChatInput> match)
        {
            var index = _messages.FindIndex(match);
            if (index == -1) return false;

            _messages.RemoveAt(index);
            return true;
        }
    }

    public class Plan : IPlan
    {
        private readonly List<PlanStep> _steps = new();

        public IReadOnlyList<PlanStep> Steps => _steps;

        public void AddStep(string description)
        {
            _steps.Add(new PlanStep { Description = description });
        }
        public int IndexOf(PlanStep step) => _steps.IndexOf(step);
        public void MarkComplete(int index, string insight)
        {
            if (index < 0 || index >= _steps.Count) return;
            _steps[index].Insight = insight;
            _steps[index].IsCompleted = true;
        }

        public void Clear()
        {
            _steps.Clear();
        }
    }


    public class AgentMemory : IAgentMemory
    {
        public IPrompt ChatHistory { get; }
        public IScratchpad Scratchpad { get; }

        public AgentMemory(IPrompt? prompt = null, IScratchpad? scratchpad = null)
        {
            ChatHistory = prompt ?? new Prompt();
            Scratchpad = scratchpad ?? new Scratchpad();
        }

        public void Clear()
        {
            ChatHistory.Clear();
            Scratchpad.Clear();
        }
    }


}
