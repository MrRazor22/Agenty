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

        private static JsonNode Canonicalize(JsonNode? node)
        {
            switch (node)
            {
                case JsonObject obj:
                    var newObj = new JsonObject();
                    foreach (var kvp in obj.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                    {
                        newObj[kvp.Key] = Canonicalize(kvp.Value);
                    }
                    return newObj;

                case JsonArray arr:
                    var newArr = new JsonArray();
                    foreach (var item in arr)
                    {
                        newArr.Add(Canonicalize(item));
                    }
                    return newArr;

                default:
                    // For primitives: return a *new* JsonValue, not the same node
                    return node is null ? null! : JsonValue.Create(node.GetValue<object>());
            }
        }


        public static string AsJSONString(this object? obj)
        {
            if (obj == null) return "<null>";
            return obj is string s ? s : JsonSerializer.Serialize(obj);
        }
    }
}
