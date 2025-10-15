using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
