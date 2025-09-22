using Agenty.LLMCore.JsonSchema;
using Agenty.LLMCore.Messages;
using Agenty.LLMCore.ToolHandling;
using System.Text.Json;

namespace Agenty.LLMCore.ChatHandling
{
    [Flags]
    public enum ChatFilter
    {
        None = 0,
        System = 1 << 0,
        User = 1 << 1,
        Assistant = 1 << 2,
        ToolCalls = 1 << 3,   // the assistant’s tool call (name + args)
        ToolResults = 1 << 4, // tool output (Tool role content)
        All = System | User | Assistant | ToolCalls | ToolResults
    }
    public static class ConversationExtensions
    {
        public static string ToJson(this Conversation chat, ChatFilter filter = ChatFilter.All)
        {
            var items = new List<object>();

            foreach (var c in chat)
            {
                // Apply role filters
                if (c.Role == Role.System && !filter.HasFlag(ChatFilter.System)) continue;
                if (c.Role == Role.User && !filter.HasFlag(ChatFilter.User)) continue;
                if (c.Role == Role.Assistant && !filter.HasFlag(ChatFilter.Assistant) && !filter.HasFlag(ChatFilter.ToolCalls)) continue;
                if (c.Role == Role.Tool && !filter.HasFlag(ChatFilter.ToolResults)) continue;

                var obj = new Dictionary<string, object?>
                {
                    ["role"] = c.Role.ToString().ToLower()
                };

                switch (c.Content)
                {
                    case TextContent text:
                        obj["content"] = text.Text;
                        break;

                    case ToolCall call when filter.HasFlag(ChatFilter.ToolCalls):
                        obj["tool_calls"] = new[]
                        {
                            new Dictionary<string, object?>
                            {
                                ["id"] = call.Id,
                                ["name"] = call.Name,
                                ["arguments"] = JsonSerializer.Deserialize<Dictionary<string, object>>(
                                    call.Arguments?.ToJsonString() ?? "{}")
                            }
                        };
                        break;

                    case ToolCallResult result when filter.HasFlag(ChatFilter.ToolResults):
                        obj["tool_result"] = result.Error != null
                            ? $"Tool execution error: {result.Error.Message}"
                            : result.Result?.ToString();
                        obj["tool_id"] = result.Call.Id;
                        obj["tool_name"] = result.Call.Name;
                        break;
                }

                items.Add(obj);
            }

            return JsonSerializer.Serialize(items, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }

        public static bool IsLastAssistantMessageSame(this Conversation chat, string? newMessage)
        {
            if (string.IsNullOrWhiteSpace(newMessage))
                return false;

            var last = chat.LastOrDefault(m =>
                m.Role == Role.Assistant &&
                m.Content is TextContent tc &&
                !string.IsNullOrWhiteSpace(tc.Text));

            return last?.Content is TextContent lastText &&
                   string.Equals(lastText.Text.Trim(), newMessage.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsToolAlreadyCalled(this Conversation chat, ToolCall toolCall)
        {
            var argKey = toolCall.Arguments?.NormalizeArgs() ?? "";

            return chat.Any(m =>
                m.Role == Role.Assistant &&
                m.Content is ToolCall tc &&
                tc.Name == toolCall.Name &&
                tc.Arguments.NormalizeArgs() == argKey);
        }

        public static string? GetLastToolCallResult(this Conversation chat, ToolCall toolCall)
        {
            var argKey = toolCall.Arguments?.NormalizeArgs() ?? "";

            var lastResult = chat.LastOrDefault(m =>
                m.Role == Role.Tool &&
                m.Content is ToolCallResult tr &&
                tr.Call.Name == toolCall.Name &&
                tr.Call.Arguments.NormalizeArgs() == argKey);

            return lastResult?.Content is ToolCallResult result
                ? result.Error != null
                    ? $"Tool execution error: {result.Error.Message}"
                    : result.Result?.ToString()
                : null;
        }

        public static Conversation AppendToolResults(this Conversation chat, IEnumerable<ToolCallResult> results)
        {
            foreach (var r in results)
                chat.Add(Role.Tool, r);
            return chat;
        }

        /// <summary>
        /// Returns the text of the most recent user message in the conversation.
        /// </summary>
        public static string? GetLastUserMessage(this Conversation chat)
        {
            var lastUser = chat.LastOrDefault(m => m.Role == Role.User);
            if (lastUser?.Content is TextContent text)
                return text.Text;

            return null;
        }

        /// <summary>
        /// Returns a sub-conversation scoped from the last user message onward,
        /// excluding system messages.
        /// </summary>
        public static Conversation GetScopedFromLastUser(this Conversation chat)
        {
            var lastUserIndex = chat.FindLastIndex(m => m.Role == Role.User);
            if (lastUserIndex < 0)
                throw new InvalidOperationException("No user message found in conversation.");

            return new Conversation(
                chat.Skip(lastUserIndex).Where(m => m.Role != Role.System)
            );
        }

    }
}
