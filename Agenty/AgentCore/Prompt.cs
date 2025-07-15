using Agenty.LLMCore;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Agenty.AgentCore
{
    public class Prompt : IPrompt, IEnumerable<ChatInput>
    {
        private readonly List<ChatInput> _messages = new();
        private readonly Func<Action<string>?> _onUpdate = null;
        public Prompt() { }
        public Prompt(string message)
        {
            Add(ChatRole.User, message);
        }
        public Prompt(Func<Action<string>?> onUpdate)
        {
            _onUpdate = onUpdate;
        }
        public IEnumerable<ChatInput> Messages => _messages;

        public void Add(ChatRole role, string content, ToolCallInfo? toolCallInfo = null)
        {
            _messages.Add(new ChatInput(role, content, toolCallInfo));
            if (_onUpdate != null) _onUpdate()?.Invoke($"[{role}]: {content}");
        }

        public void AddMany(params (ChatRole role, string content)[] messages)
        {
            foreach (var (role, content) in messages)
                Add(role, content);
        }

        public void AddMany(params (ChatRole role, string content, ToolCallInfo? toolCallInfo)[] messages)
        {
            foreach (var (role, content, toolCallInfo) in messages)
                Add(role, content, toolCallInfo);
        }

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
        public void Clear() => _messages.Clear();

        public IEnumerator<ChatInput> GetEnumerator()
        {
            return Messages.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable)Messages).GetEnumerator();
        }
    }
}
