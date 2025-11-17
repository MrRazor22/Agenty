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
    }
}
