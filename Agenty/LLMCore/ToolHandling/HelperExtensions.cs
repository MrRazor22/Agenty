using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Agenty.LLMCore.ToolHandling
{
    internal static class HelperExtensions
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
    }
}
