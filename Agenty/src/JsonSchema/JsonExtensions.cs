using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace Agenty.LLMCore.JsonSchema
{
    public static class JsonExtensions
    {
        public static string NormalizeArgs(this JObject args) =>
    Canonicalize(args).ToString(Formatting.None);

        private static JToken Canonicalize(JToken? node)
        {
            switch (node)
            {
                case JObject obj:
                    var newObj = new JObject();
                    foreach (var kvp in obj.Properties().OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        newObj[kvp.Name] = Canonicalize(kvp.Value);
                    }
                    return newObj;

                case JArray arr:
                    var newArr = new JArray();
                    foreach (var item in arr)
                    {
                        newArr.Add(Canonicalize(item));
                    }
                    return newArr;

                case JValue val when val.Type == JTokenType.String:
                    var s = val.Value<string>() ?? "";
                    // trim, collapse multiple spaces, lowercase
                    s = Regex.Replace(s.Trim(), @"\s+", " ").ToLowerInvariant();
                    return JValue.CreateString(s);

                default:
                    return node?.DeepClone() ?? JValue.CreateNull();
            }
        }


        public static string AsJsonString(this object? obj)
        {
            if (obj == null) return "<null>";
            return obj is string s ? s : JsonConvert.SerializeObject(obj);
        }
    }
}
