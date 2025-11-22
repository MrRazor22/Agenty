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
        public IChatContent Content { get; }

        [JsonConstructor]
        private Chat(Role role, JToken content)
        {
            Role = role;

            if (content.Type == JTokenType.String)
                Content = new TextContent(content.Value<string>());
            else if (content is JObject obj && obj["Text"] != null)
                Content = new TextContent((string)obj["Text"]);
            else
                throw new Exception("Unknown content type.");
        }
        public Chat(Role role, IChatContent content)
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

        public Conversation Add(Role role, TextContent text) => Add(role, (IChatContent)text);

        public Conversation Add(Role role, IChatContent content)
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
