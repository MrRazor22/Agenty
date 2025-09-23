using Agenty.LLMCore.Messages;

namespace Agenty.LLMCore.ChatHandling
{
    public enum Role { System, Assistant, User, Tool }

    public record Chat(Role Role, IMessageContent Content);

    public class Conversation : List<Chat>
    {
        public Conversation() { }
        public Conversation(IEnumerable<Chat> messages)
        {
            foreach (var msg in messages) Add(msg.Role, msg.Content);
        }

        public event Action<Chat>? OnChat;

        public Conversation Add(Role role, TextContent text) => Add(role, (IMessageContent)text);

        public Conversation Add(Role role, IMessageContent content)
        {
            if (content == null) return this;

            var chat = new Chat(role, content);
            base.Add(chat);
            OnChat?.Invoke(chat);

            return this;
        }

        public static Conversation Clone(Conversation original)
        {
            var copy = new Conversation();
            foreach (var message in original)
                copy.Add(message.Role, message.Content);
            return copy;
        }

        public Conversation Append(Conversation other, ChatFilter filter = ChatFilter.All)
        {
            foreach (var chat in other)
            {
                if (chat.Role == Role.System && !filter.HasFlag(ChatFilter.System)) continue;
                if (chat.Role == Role.User && !filter.HasFlag(ChatFilter.User)) continue;
                if (chat.Role == Role.Assistant && !filter.HasFlag(ChatFilter.Assistant)) continue;
                if (chat.Role == Role.Tool && !filter.HasFlag(ChatFilter.ToolResults)) continue;

                Add(chat.Role, chat.Content);
            }
            return this;
        }

    }
}
