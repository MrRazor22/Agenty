using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;

namespace Agenty.LLMCore
{
    public enum Role { System, Assistant, User, Tool }
    public record Chat(Role Role, string? Content, ToolCall? toolCallInfo = null);
    public class ChatHistory : List<Chat>
    {
        public event Action<Chat>? OnChat;
        public ChatHistory Add(Role role, string? content = null, ToolCall? tool = null)
        {
            var chat = new Chat(role, content, tool);
            Add(chat);
            OnChat?.Invoke(chat);
            return this;
        }
        public static ChatHistory Clone(ChatHistory original)
        {
            var copy = new ChatHistory();
            foreach (var message in original)
            {
                copy.Add(message.Role, message.Content, message.toolCallInfo);
            }
            return copy;
        }
    }
}
