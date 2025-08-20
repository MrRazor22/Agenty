using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Agenty.LLMCore.JsonSchema
{
    public static class JsonHelper
    {
        public static readonly JsonSerializerOptions DefaultOptions = new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
            PropertyNameCaseInsensitive = true
        };

        // Generic deserialize
        public static T? DeserializeJson<T>(string jsonString)
        {
            var targetType = typeof(T);

            // Primitive or simple types
            if (targetType.IsPrimitive || targetType == typeof(string) || targetType == typeof(decimal))
                return JsonSerializer.Deserialize<T>(jsonString);

            // Array or List<>
            if ((targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(List<>)) ||
                targetType.IsArray)
            {
                return JsonSerializer.Deserialize<T>(jsonString, DefaultOptions);
            }

            // Dictionary types
            if (targetType.IsGenericType &&
                (targetType.GetGenericTypeDefinition() == typeof(Dictionary<,>) ||
                 targetType.GetGenericTypeDefinition() == typeof(IDictionary<,>)))
            {
                return JsonSerializer.Deserialize<T>(jsonString, DefaultOptions);
            }

            // Normal object
            return JsonSerializer.Deserialize<T>(jsonString, DefaultOptions);
        }

        // Non-generic version
        public static object? DeserializeJson(string jsonString, Type targetType)
        {
            return JsonSerializer.Deserialize(jsonString, targetType, DefaultOptions);
        }

    }


}
