using Agenty.ToolHandling;
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
        [Tool]
        [Description("Quick search the internet.")]
        public static async Task<string> Search(
            [Description("Search query text")] string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return "Error: Empty query.";

            var url = $"https://api.duckduckgo.com/?q={Uri.EscapeDataString(query)}&format=json&no_html=1&skip_disambig=1";
            try
            {
                var json = await _http.GetStringAsync(url);
                var node = JsonNode.Parse(json);

                // 1. Try Abstract
                var abstractText = node?["Abstract"]?.ToString();
                if (!string.IsNullOrWhiteSpace(abstractText))
                    return abstractText;

                // 2. Try RelatedTopics[0].Text
                var related = node?["RelatedTopics"]?.AsArray()?.FirstOrDefault()?["Text"]?.ToString();
                if (!string.IsNullOrWhiteSpace(related))
                    return related;

                // 3. Try Heading
                var heading = node?["Heading"]?.ToString();
                if (!string.IsNullOrWhiteSpace(heading))
                    return $"No direct answer. Related topic: {heading}";

                // 4. Fallback
                return $"No instant answer found for: {query}";
            }
            catch (Exception ex)
            {
                return $"Search failed: {ex.Message}";
            }
        }

        [Tool]
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
