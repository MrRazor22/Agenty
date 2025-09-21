// File: Agenty/RAG/IO/WebSearchLoader.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Agenty.RAG.IO
{
    public static class WebSearchLoader
    {
        private static readonly string ApiUrl = "https://en.wikipedia.org/w/api.php";

        /// <summary>
        /// Default web search loader (currently powered by Wikipedia API).
        /// Returns (Doc, Source) pairs for a query.
        /// </summary>
        public static async Task<IReadOnlyList<(string Doc, string Source)>> SearchAsync(string query, int maxResults = 3)
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; WikipediaLoader/1.0)");

            var url = $"{ApiUrl}?action=query&list=search&srsearch={Uri.EscapeDataString(query)}&utf8=&format=json&srlimit={maxResults}";
            var resp = await http.GetAsync(url);
            resp.EnsureSuccessStatusCode();

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            var results = new List<(string, string)>();

            foreach (var item in doc.RootElement
                                     .GetProperty("query")
                                     .GetProperty("search")
                                     .EnumerateArray())
            {
                var title = item.GetProperty("title").GetString() ?? "";
                var pageUrl = $"https://en.wikipedia.org/wiki/{Uri.EscapeDataString(title.Replace(' ', '_'))}";

                // Fetch page extract
                var extractUrl = $"{ApiUrl}?action=query&prop=extracts&explaintext=true&titles={Uri.EscapeDataString(title)}&format=json";
                var extractResp = await http.GetStringAsync(extractUrl);
                using var extractDoc = JsonDocument.Parse(extractResp);

                foreach (var page in extractDoc.RootElement
                                               .GetProperty("query")
                                               .GetProperty("pages")
                                               .EnumerateObject())
                {
                    if (page.Value.TryGetProperty("extract", out var extract))
                    {
                        var text = extract.GetString() ?? "";
                        if (!string.IsNullOrWhiteSpace(text))
                            results.Add((text, pageUrl));
                    }
                }
            }

            return results;
        }
    }
}
