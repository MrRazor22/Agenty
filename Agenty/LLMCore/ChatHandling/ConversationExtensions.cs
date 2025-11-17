using Agenty.AgentCore.TokenHandling;
using Agenty.LLMCore.JsonSchema;
using Agenty.LLMCore.Messages;
using Agenty.LLMCore.ToolHandling;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Agenty.LLMCore.ChatHandling
{
    [Flags]
    public enum ChatFilter
    {
        None = 0,
        System = 1 << 0,
        User = 1 << 1,
        Assistant = 1 << 2,
        ToolCalls = 1 << 3,
        ToolResults = 1 << 4,
        All = System | User | Assistant | ToolCalls | ToolResults
    }

    public static class ConversationExtensions
    {
        public static Conversation AddUser(this Conversation convo, string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return convo;
            return convo.Add(Role.User, new TextContent(text!));
        }

        public static Conversation AddSystem(this Conversation convo, string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return convo;
            return convo.Add(Role.System, new TextContent(text!));
        }

        public static Conversation AddAssistant(this Conversation convo, string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return convo;
            return convo.Add(Role.Assistant, new TextContent(text!));
        }

        public static Conversation AddAssistantToolCall(this Conversation convo, ToolCall? call)
        {
            if (call == null) return convo;
            return convo.Add(Role.Assistant, call);
        }

        public static Conversation AddToolResult(this Conversation convo, ToolCallResult? result)
        {
            if (result == null) return convo;
            return convo.Add(Role.Tool, result);
        }

        public static Conversation Clone(this Conversation target, Conversation source, ChatFilter filter = ChatFilter.All)
        {
            target.Clear();

            foreach (var message in source)
            {
                if (!ShouldInclude(message, filter)) continue;
                target.Add(message.Role, message.Content);
            }

            return target;
        }
        public static Conversation Clone(this Conversation source, ChatFilter filter = ChatFilter.All)
        {
            var copy = new Conversation();

            foreach (var message in source)
            {
                if (!ShouldInclude(message, filter)) continue;
                copy.Add(message.Role, message.Content);
            }

            return copy;
        }
        public static string ToJson(this Conversation chat, ChatFilter filter = ChatFilter.All)
        {
            var items = new List<object>();

            foreach (var c in chat)
            {
                if (!ShouldInclude(c, filter))
                    continue;

                var obj = new Dictionary<string, object>();
                obj["role"] = c.Role.ToString().ToLowerInvariant();

                var text = c.Content as TextContent;
                if (text != null)
                {
                    obj["content"] = text.Text;
                }

                var call = c.Content as ToolCall;
                if (call != null && (filter & ChatFilter.ToolCalls) != 0)
                {
                    obj["tool_calls"] = new object[]
                    {
                        new Dictionary<string, object>
                        {
                            { "id", call.Id },
                            { "name", call.Name },
                            { "arguments", JObject.Parse(call.Arguments != null ? call.Arguments.ToString(Newtonsoft.Json.Formatting.None) : "{}") }
                        }
                    };
                }

                var result = c.Content as ToolCallResult;
                if (result != null && (filter & ChatFilter.ToolResults) != 0)
                {
                    obj["tool_result"] = result.Result;
                    obj["tool_id"] = result.Call.Id;
                    obj["tool_name"] = result.Call.Name;
                }

                items.Add(obj);
            }

            return JsonConvert.SerializeObject(items, Formatting.Indented);
        }

        public static bool IsLastAssistantMessageSame(this Conversation chat, string newMessage)
        {
            if (string.IsNullOrWhiteSpace(newMessage))
                return false;

            var last = chat.LastOrDefault(m =>
                m.Role == Role.Assistant &&
                m.Content is TextContent &&
                !string.IsNullOrWhiteSpace(((TextContent)m.Content).Text));

            if (last == null) return false;

            var lastText = last.Content as TextContent;
            return lastText != null &&
                   string.Equals(lastText.Text.Trim(), newMessage.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsToolAlreadyCalled(this Conversation chat, ToolCall toolCall)
        {
            var argKey = toolCall.Arguments != null ? toolCall.Arguments.NormalizeArgs() : "";

            return chat.Any(m =>
                m.Content is ToolCall &&
                ((ToolCall)m.Content).Name == toolCall.Name &&
                ((ToolCall)m.Content).Arguments.NormalizeArgs() == argKey);
        }
        public static bool ExistsIn(this ToolCall call, Conversation chat, IEnumerable<ToolCall>? also = null)
        {
            var key = call.Arguments?.NormalizeArgs() ?? "";

            return (also?.Any(c =>
                        c.Name == call.Name &&
                        (c.Arguments?.NormalizeArgs() ?? "") == key) ?? false)
                   || chat.Any(m =>
                        m.Content is ToolCall t &&
                        t.Name == call.Name &&
                        (t.Arguments?.NormalizeArgs() ?? "") == key);
        }


        public static object? GetLastToolCallResult(this Conversation chat, ToolCall toolCall)
        {
            var argKey = toolCall.Arguments != null ? toolCall.Arguments.NormalizeArgs() : "";

            var lastResult = chat.LastOrDefault(m =>
                m.Role == Role.Tool &&
                m.Content is ToolCallResult &&
                ((ToolCallResult)m.Content).Call.Name == toolCall.Name &&
                ((ToolCallResult)m.Content).Call.Arguments.NormalizeArgs() == argKey);

            if (lastResult == null) return null;

            var result = lastResult.Content as ToolCallResult;
            if (result == null) return null;

            return result.Result;
        }

        public static Conversation AppendToolCallAndResults(this Conversation chat, IEnumerable<ToolCallResult> results)
        {
            foreach (var r in results)
            {
                chat.AddAssistantToolCall(r.Call);
                chat.AddToolResult(r);
            }
            return chat;
        }

        public static string? GetCurrentUserRequest(this Conversation chat)
        {
            var lastUser = chat.LastOrDefault(m => m.Role == Role.User);
            var text = lastUser != null ? lastUser.Content as TextContent : null;
            return text != null ? text.Text : null;
        }

        public static Conversation GetScopedFromLastUser(this Conversation chat)
        {
            var lastUserIndex = chat.FindLastIndex(m => m.Role == Role.User);
            if (lastUserIndex < 0)
                throw new InvalidOperationException("No user message found in conversation.");

            return new Conversation(
                chat.Skip(lastUserIndex).Where(m => m.Role != Role.System)
            );
        }
        private static bool ShouldInclude(Chat chat, ChatFilter filter)
        {
            return chat.Role switch
            {
                Role.System => (filter & ChatFilter.System) != 0,
                Role.User => (filter & ChatFilter.User) != 0,
                Role.Tool => (filter & ChatFilter.ToolResults) != 0,
                Role.Assistant => chat.Content switch
                {
                    ToolCall _ => (filter & ChatFilter.ToolCalls) != 0,
                    TextContent _ => (filter & ChatFilter.Assistant) != 0,
                    _ => false
                },
                _ => false
            };
        }

    }
}
