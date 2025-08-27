namespace Agenty.LLMCore
{
    public enum Role { System, Assistant, User, Tool }

    public record Chat(Role Role, string? Content, ToolCall? toolCallInfo = null, bool IsTemporary = false);

    public class Conversation : List<Chat>
    {
        public event Action<Chat>? OnChat;

        public Conversation Add(Role role, string? content = null, ToolCall? tool = null, bool isTemporary = false)
        {
            // If both content and tool are "empty", just ignore
            if (string.IsNullOrWhiteSpace(content) && (tool == null || string.IsNullOrWhiteSpace(tool.Name) && string.IsNullOrWhiteSpace(tool.AssistantMessage)))
                return this;

            var chat = new Chat(role, content, tool, isTemporary);
            base.Add(chat);
            OnChat?.Invoke(chat);

            // Clear temporary placeholders when assistant commits a real message
            if (role == Role.Assistant)
                RemoveAll(c => c.IsTemporary);

            return this;
        }

        public static Conversation Clone(Conversation original)
        {
            var copy = new Conversation();
            foreach (var message in original)
            {
                copy.Add(message.Role, message.Content, message.toolCallInfo, message.IsTemporary);
            }
            return copy;
        }
        public Conversation Append(Conversation other, bool includeSystem = false)
        {
            foreach (var chat in other)
            {
                if (!includeSystem && chat.Role == Role.System)
                    continue;

                Add(chat.Role, chat.Content, chat.toolCallInfo, chat.IsTemporary);
            }
            return this;
        }
    }
}
