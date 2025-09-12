using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Agenty.LLMCore.JsonSchema
{
    public static class JsonExtensions
    {
        public static string NormalizeArgs(this JsonObject args) =>
        Canonicalize(args).ToJsonString(new JsonSerializerOptions { WriteIndented = false });

        private static JsonNode Canonicalize(JsonNode? node) =>
            node switch
            {
                JsonObject obj => new JsonObject(
                    obj.OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                       .Select(kvp => new KeyValuePair<string, JsonNode?>(kvp.Key, Canonicalize(kvp.Value)))
                ),
                JsonArray arr => new JsonArray(arr.Select(Canonicalize).ToArray()),
                _ => node!
            };

        public static string AsJSONString(this object? obj)
        {
            if (obj == null) return "<null>";
            return obj is string s ? s : JsonSerializer.Serialize(obj);
        }
    }
}
