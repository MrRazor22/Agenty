
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Agenty.Utilities
{
    public static class JsonRepair
    {
        public static JsonObject? RepairAndParse(string input)
        {
            // Step 1: Try parse as-is
            if (TryParse(input, out var json))
                return json;

            // Step 2: Extract closest JSON object using regex
            var match = Regex.Match(input, @"\{[\s\S]*?\}", RegexOptions.Singleline);
            if (!match.Success)
                return null;

            string candidate = match.Value;

            // Step 3: Try various cleanup strategies
            candidate = RemoveTrailingCommas(candidate);
            candidate = FixUnquotedKeys(candidate);

            // Final parse attempt
            if (TryParse(candidate, out var repairedJson))
                return repairedJson;

            return null;
        }

        private static bool TryParse(string input, out JsonObject? json)
        {
            try
            {
                var parsed = JsonNode.Parse(input);
                json = parsed as JsonObject;
                return json != null;
            }
            catch
            {
                json = null;
                return false;
            }
        }

        private static string RemoveTrailingCommas(string input)
        {
            // Remove trailing commas before closing brackets/braces
            return Regex.Replace(input, @",(\s*[}\]])", "$1");
        }

        private static string FixUnquotedKeys(string input)
        {
            // Match: { name: "xyz" } → { "name": "xyz" }
            return Regex.Replace(input, @"(?<=\{|,)\s*(\w+)\s*:", @"""$1"":");
        }
    }

}
