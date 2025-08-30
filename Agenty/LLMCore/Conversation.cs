using Agenty.LLMCore.ToolHandling;
using System.Text;

namespace Agenty.LLMCore
{
    public enum Role { System, Assistant, User, Tool }

    public record Chat(Role Role, string? Content, List<ToolCall>? ToolCalls = null, bool IsTemporary = false);

    public class Conversation : List<Chat>
    {
        public event Action<Chat>? OnChat;

        // Overload for single tool call
        public Conversation Add(Role role, string? content, ToolCall toolCall, bool isTemporary = false)
        {
            if (toolCall == null && string.IsNullOrWhiteSpace(content))
                return this;

            return Add(role, content,
                toolCall != null ? new List<ToolCall> { toolCall } : null,
                isTemporary);
        }

        public Conversation Add(Role role, string? content = null, List<ToolCall>? toolCalls = null, bool isTemporary = false)
        {
            if (string.IsNullOrWhiteSpace(content) && (toolCalls == null || toolCalls.Count == 0))
                return this;

            var chat = new Chat(role, content, toolCalls, isTemporary);
            base.Add(chat);
            OnChat?.Invoke(chat);

            if (role == Role.Assistant)
                RemoveAll(c => c.IsTemporary);

            return this;
        }

        public static Conversation Clone(Conversation original)
        {
            var copy = new Conversation();
            foreach (var message in original)
            {
                copy.Add(message.Role, message.Content, message.ToolCalls, message.IsTemporary);
            }
            return copy;
        }
        public Conversation Append(Conversation other, bool includeSystem = false)
        {
            foreach (var chat in other)
            {
                if (!includeSystem && chat.Role == Role.System)
                    continue;

                Add(chat.Role, chat.Content, chat.ToolCalls, chat.IsTemporary);
            }
            return this;
        }

        public string ToHistoyString(bool includeSystem = true)
        {
            var sb = new StringBuilder();

            foreach (var chat in this)
            {
                if (!includeSystem && chat.Role == Role.System)
                    continue;

                sb.Append('[').Append(chat.Role).Append("] ");

                if (!string.IsNullOrWhiteSpace(chat.Content))
                {
                    sb.AppendLine(chat.Content.Trim());
                }
                else if (chat.ToolCalls != null && chat.ToolCalls.Count > 0)
                {
                    foreach (var call in chat.ToolCalls)
                        sb.AppendLine(call.ToString()); // relies on ToolCall.ToString()
                }
                else
                {
                    sb.AppendLine("<empty>");
                }
            }

            return sb.ToString();
        }
    }
}
