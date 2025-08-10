using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using static Agenty.Program;

namespace Agenty.LLMCore.BuiltInTools
{
    static class SearchTools
    {

        [Description("Gets a summary of a Wikipedia topic.")]
        public static async Task<string> WikiSummary([Description("Title of the Wikipedia article")] string topic)
        {
            using var client = new HttpClient();

            try
            {
                var searchUrl = $"https://en.wikipedia.org/w/api.php?action=query&list=search&srsearch={Uri.EscapeDataString(topic)}&format=json";
                var searchJson = await client.GetStringAsync(searchUrl);
                var searchObj = JsonNode.Parse(searchJson);
                var title = searchObj?["query"]?["search"]?[0]?["title"]?.ToString();

                if (string.IsNullOrWhiteSpace(title))
                    return "No matching Wikipedia article found.";

                var summaryUrl = $"https://en.wikipedia.org/api/rest_v1/page/summary/{Uri.EscapeDataString(title)}";
                var summaryJson = client.GetStringAsync(summaryUrl).Result;
                var summaryObj = JsonNode.Parse(summaryJson);
                return summaryObj?["extract"]?.ToString() ?? "No summary found.";
            }
            catch
            {
                return "Failed to fetch Wikipedia data.";
            }
        }
    }
}
