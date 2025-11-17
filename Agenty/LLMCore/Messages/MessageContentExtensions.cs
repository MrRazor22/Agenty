using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Agenty.LLMCore.Messages
{
    public static class MessageContentExtensions
    {
        public static string AsReadable(this IMessageContent content)
        {
            if (content == null)
                return "<empty>";

            // plain assistant text
            if (content is TextContent txt && !string.IsNullOrWhiteSpace(txt.Text))
                return txt.Text.Trim(); // keep original newlines untouched

            // stringify ToolCall or any JSON content
            try
            {
                return content.AsPrettyJson();
            }
            catch
            {
                return content.ToString() ?? "<unknown>";
            }
        }

        public static string AsPrettyJson(this IMessageContent content)
        {
            if (content == null)
                return "<empty>";

            // If it directly is a JToken payload (rare)
            if (content is JToken jt)
                return jt.ToString(Formatting.Indented);

            // If it's a ToolCall — pretty its arguments
            if (content is ToolCall tc)
            {
                if (tc.Arguments != null)
                    return tc.Arguments.ToString(Formatting.Indented);
            }

            // fallback: serialize the object
            var json = JsonConvert.SerializeObject(content, Formatting.Indented);
            return json ?? "<unknown>";
        }
    }
}
