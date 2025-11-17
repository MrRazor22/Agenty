using Agenty.LLMCore.Messages;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace Agenty.LLMCore.ChatHandling
{
    public enum Role { System, Assistant, User, Tool }

    public class Chat
    {
        public Role Role { get; }
        public IMessageContent Content { get; }

        [JsonConstructor] // ← this is the whole fix
        private Chat(Role role, JObject content)
        {
            Role = role;

            // minimal factory for ONE content type (TextContent)
            if (content["Text"] != null)
                Content = new TextContent((string)content["Text"]);
            else
                throw new Exception("Unknown content type.");
        }
        public Chat(Role role, IMessageContent content)
        {
            Role = role;
            Content = content;
        }
        public Chat(Role role, string content)
        {
            Role = role;
            Content = new TextContent(content);
        }
    }

    public class Conversation : List<Chat>
    {
        public Conversation() { }
        public Conversation(IEnumerable<Chat> messages)
        {
            foreach (var msg in messages) Add(msg.Role, msg.Content);
        }

        public event Action<Chat>? OnChat;
        public Conversation Add(Role role, string text) => Add(role, new TextContent(text));

        public Conversation Add(Role role, TextContent text) => Add(role, (IMessageContent)text);

        public Conversation Add(Role role, IMessageContent content)
        {
            if (content == null) return this;

            var chat = new Chat(role, content);
            base.Add(chat);
            OnChat?.Invoke(chat);

            return this;
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
