using Agenty.LLMCore.ChatHandling;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Agenty.Utils
{
    public static class Helpers
    {
        public static string AsPrettyJson(this object content)
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
        public static string ToJoinedString<T>(
            this IEnumerable<T> source,
            string separator = "\n")
        {
            if (source == null) return "<null>";
            var list = source.ToList();
            return list.Count > 0
                ? string.Join(separator, list.Select(x => x?.ToString()))
                : "<empty>";
        }

        public static bool TryParseCompleteJson(this string json, out JObject? result)
        {
            result = null;
            try
            {
                result = JObject.Parse(json);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
