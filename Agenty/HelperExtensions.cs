using Agenty.LLMCore.Messages;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Agenty
{
    internal static class Helpers
    {
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

        public static string AsPrettyJson(this object? obj)
        {
            if (obj is null)
                return "null";

            var settings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore,
                Converters = { new StringEnumConverter() }
            };

            // flatten ToolCallResult for cleaner logs
            if (obj is ToolCallResult t)
                obj = new { CallId = t.Call.Id, Result = t.Result ?? new JObject() };

            return JsonConvert.SerializeObject(obj, settings);
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
