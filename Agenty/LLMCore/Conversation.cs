using Agenty.LLMCore.JsonSchema;
using Agenty.LLMCore.ToolHandling;
using System.Text;

namespace Agenty.LLMCore
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

        public string ToString(ChatFilter filter = ChatFilter.All)
        {
            var sb = new StringBuilder();

            foreach (var chat in this)
            {
                // Role-level include check
                if (chat.Role == Role.System && !filter.HasFlag(ChatFilter.System)) continue;
                if (chat.Role == Role.User && !filter.HasFlag(ChatFilter.User)) continue;
                if (chat.Role == Role.Assistant && !filter.HasFlag(ChatFilter.Assistant) && !filter.HasFlag(ChatFilter.ToolCalls)) continue;
                if (chat.Role == Role.Tool && !filter.HasFlag(ChatFilter.ToolResults)) continue;

                // If we only want tool calls/results, skip non-matching roles
                // (Assistant may still emit tool calls, so handle below)
                if (filter.HasFlag(ChatFilter.ToolCalls) || filter.HasFlag(ChatFilter.ToolResults))
                {
                    // If chat has toolcalls, and ToolCalls flag is set, print them
                    if (chat.ToolCalls != null && chat.ToolCalls.Count > 0)
                    {
                        if (filter.HasFlag(ChatFilter.ToolCalls))
                        {
                            foreach (var call in chat.ToolCalls)
                            {
                                // Show assistant tool-call only if assistant included OR ToolCalls explicitly requested
                                sb.Append("Assistant (ToolCall): ")
                                  .AppendLine(call.ToString());
                            }
                        }
                    }

                    // If this is a Tool role and ToolResults requested, print content
                    if (chat.Role == Role.Tool && filter.HasFlag(ChatFilter.ToolResults))
                    {
                        if (!string.IsNullOrWhiteSpace(chat.Content))
                            sb.Append("Tool").Append(" (").Append(chat.ToolCalls?.FirstOrDefault()?.Name ?? "result").Append("): ")
                              .AppendLine(chat.Content.Trim());
                    }

                    // Continue to next chat — when using Tool* flags we avoid duplicate printing of normal content
                    // Unless the caller also asked for Assistant/User/System content explicitly
                    if (!filter.HasFlag(ChatFilter.Assistant) && !filter.HasFlag(ChatFilter.User) && !filter.HasFlag(ChatFilter.System))
                        continue;
                }

                // Normal content printing for System/User/Assistant (non-tool content)
                if (!string.IsNullOrWhiteSpace(chat.Content))
                {
                    // skip tool role content if ToolResults not requested (already handled above)
                    if (chat.Role == Role.Tool && !filter.HasFlag(ChatFilter.ToolResults)) continue;

                    sb.Append(chat.Role).Append(": ")
                      .AppendLine(chat.Content.Trim());
                }
                else if (chat.ToolCalls != null && chat.ToolCalls.Count > 0)
                {
                    // If ToolCalls requested and Assistant/User/System also requested, show them inline
                    if (filter.HasFlag(ChatFilter.ToolCalls) && (filter.HasFlag(ChatFilter.Assistant) || filter.HasFlag(ChatFilter.System) || filter.HasFlag(ChatFilter.User)))
                    {
                        foreach (var call in chat.ToolCalls)
                            sb.Append(chat.Role == Role.Assistant ? "Assistant (ToolCall): " : $"{chat.Role} (ToolCall): ")
                              .AppendLine(call.ToString());
                    }
                }
                else
                {
                    sb.Append(chat.Role).Append(": <empty>").AppendLine();
                }
            }

            return sb.ToString();
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
