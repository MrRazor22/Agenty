using Agenty.JsonSchema;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Agenty.ChatHandling
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
                            { "arguments", JObject.Parse(call.Arguments != null ? call.Arguments.ToString(Formatting.None) : "{}") }
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
        public static IEnumerable<Chat> Filter(this Conversation convo, ChatFilter filter)
        {
            foreach (var msg in convo)
                if (ShouldInclude(msg, filter))
                    yield return msg;
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
        public static List<Dictionary<string, object>> ToLogList(this Conversation convo)
        {
            var list = new List<Dictionary<string, object>>();

            foreach (var chat in convo)
            {
                var item = new Dictionary<string, object>();
                item["role"] = chat.Role.ToString();

                object contentObj;

                // TextContent
                TextContent txt = chat.Content as TextContent;
                if (txt != null)
                {
                    contentObj = txt.Text;
                }
                else
                {
                    // ToolCall
                    ToolCall call = chat.Content as ToolCall;
                    if (call != null)
                    {
                        var callDict = new Dictionary<string, object>();
                        callDict["type"] = "tool_call";
                        callDict["id"] = call.Id;
                        callDict["name"] = call.Name;
                        callDict["arguments"] = call.Arguments;

                        contentObj = callDict;
                    }
                    else
                    {
                        // ToolCallResult
                        ToolCallResult result = chat.Content as ToolCallResult;
                        if (result != null)
                        {
                            var resDict = new Dictionary<string, object>();
                            resDict["type"] = "tool_result";
                            resDict["call"] = result.Call != null ? result.Call.Name : null;
                            resDict["result"] = result.Result;

                            contentObj = resDict;
                        }
                        else
                        {
                            // fallback
                            contentObj = chat.Content != null
                                ? chat.Content.ToString()
                                : "<null>";
                        }
                    }
                }

                item["content"] = contentObj;
                list.Add(item);
            }

            return list;
        }
    }
}
