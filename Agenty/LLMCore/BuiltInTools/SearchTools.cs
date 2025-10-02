using System;
using System.ComponentModel;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Agenty.LLMCore.BuiltInTools
{
    internal static class HttpDefaults
    {
        public const string UserAgent = "Agenty.LLMCore/1.0";
    }

    class SearchTools
    {
        private static readonly HttpClient _http = new HttpClient() { Timeout = TimeSpan.FromSeconds(15) };

        static SearchTools()
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(HttpDefaults.UserAgent);
        }

        [Description("Quick search using DuckDuckGo Instant Answer API.")]
        public static async Task<string> Search(
            [Description("Search query text")] string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return "Error: Empty query.";

            var url = $"https://api.duckduckgo.com/?q={Uri.EscapeDataString(query)}&format=json&no_html=1";
            try
            {
                var json = await _http.GetStringAsync(url);
                var node = JsonNode.Parse(json);

                var abstractText = node?["Abstract"]?.ToString();
                if (!string.IsNullOrEmpty(abstractText))
                    return abstractText;

                // fallback: heading + related
                var heading = node?["Heading"]?.ToString() ?? query;
                return $"No instant answer. Related to: {heading}";
            }
            catch (Exception ex)
            {
                return $"Search failed: {ex.Message}";
            }
        }

        [Description("Get a Wikipedia summary for a given topic.")]
        public static async Task<string> GetWikiSummary(
            [Description("Topic to search on Wikipedia")] string topic,
            [Description("Language code, e.g., en, es, de")] string lang = "en")
        {
            if (string.IsNullOrWhiteSpace(lang))
                lang = "en";

            if (string.IsNullOrWhiteSpace(topic))
                return "Error: Empty topic.";

            var url = $"https://{lang}.wikipedia.org/api/rest_v1/page/summary/{Uri.EscapeDataString(topic)}";
            try
            {
                var json = await _http.GetStringAsync(url);
                var node = JsonNode.Parse(json);

                var title = node?["title"]?.ToString() ?? topic;
                var extract = node?["extract"]?.ToString() ?? "No summary available.";
                var page = node?["content_urls"]?["desktop"]?["page"]?.ToString() ?? "";

                return $"{title}: {extract}\n{page}";
            }
            catch (Exception ex)
            {
                return $"Wikipedia lookup failed: {ex.Message}";
            }
        }
    }
}
