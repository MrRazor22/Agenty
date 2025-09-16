using Agenty.LLMCore.JsonSchema;
using Agenty.LLMCore.ToolHandling;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Agenty.LLMCore.ChatHandling
{
    [Flags]
    public enum ChatFilter
    {
        None = 0,
        System = 1 << 0,
        User = 1 << 1,
        Assistant = 1 << 2,
        ToolCalls = 1 << 3,     // the assistant's tool *call* (name + args)
        ToolResults = 1 << 4,   // tool output (Tool role content)
        All = System | User | Assistant | ToolCalls | ToolResults
    }
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
            Add(chat);
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

        public string ToString(ChatFilter filter = ChatFilter.All)
        {
            var items = new List<object>();

            foreach (var chat in this)
            {
                // Skip based on filter
                if (chat.Role == Role.System && !filter.HasFlag(ChatFilter.System)) continue;
                if (chat.Role == Role.User && !filter.HasFlag(ChatFilter.User)) continue;
                if (chat.Role == Role.Assistant && !filter.HasFlag(ChatFilter.Assistant) && !filter.HasFlag(ChatFilter.ToolCalls)) continue;
                if (chat.Role == Role.Tool && !filter.HasFlag(ChatFilter.ToolResults)) continue;

                var obj = new Dictionary<string, object?>()
                {
                    ["role"] = chat.Role.ToString().ToLower()
                };

                if (!string.IsNullOrWhiteSpace(chat.Content))
                    obj["content"] = chat.Content.Trim();

                if (chat.ToolCalls != null && chat.ToolCalls.Count > 0 && filter.HasFlag(ChatFilter.ToolCalls))
                {
                    obj["tool_calls"] = chat.ToolCalls.Select(call => new Dictionary<string, object?>
                    {
                        ["id"] = call.Id,
                        ["name"] = call.Name,
                        ["arguments"] = JsonSerializer.Deserialize<Dictionary<string, object>>(call.Arguments?.ToJsonString() ?? "{}")
                    }).ToList();
                }

                if (chat.Role == Role.Tool && filter.HasFlag(ChatFilter.ToolResults))
                {
                    obj["result"] = !string.IsNullOrWhiteSpace(chat.Content) ? chat.Content.Trim() : null;
                }

                items.Add(obj);
            }

            return JsonSerializer.Serialize(items, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }


        public bool IsToolAlreadyCalled(ToolCall toolCall)
        {
            var argKey = toolCall.Arguments != null ? toolCall.Arguments.NormalizeArgs() : "";
            return this.Any(m =>
                              m.Role == Role.Assistant &&
                              m.ToolCalls?.Any(tc => tc.Name == toolCall.Name &&
                              tc.Arguments.NormalizeArgs() == argKey) == true);
        }
        public string GetLastToolCallResult(ToolCall toolCall)
        {
            var argKey = toolCall.Arguments != null ? toolCall.Arguments.NormalizeArgs() : "";
            return this.LastOrDefault(m =>
                                      m.Role == Role.Tool &&
                                      m.ToolCalls?.Any(tc => tc.Name == toolCall.Name
                                                             && (tc.Arguments?.ToJsonString() ?? "") == argKey) == true
                                  )?.Content ?? "result above";
        }
        public bool IsLastAssistantMessageSame(string? newMessage)
        {
            if (string.IsNullOrWhiteSpace(newMessage))
                return false;

            var last = this.LastOrDefault(m => m.Role == Role.Assistant && !string.IsNullOrWhiteSpace(m.Content));
            return last != null && string.Equals(last.Content.Trim(), newMessage.Trim(), StringComparison.OrdinalIgnoreCase);
        }

    }
}
