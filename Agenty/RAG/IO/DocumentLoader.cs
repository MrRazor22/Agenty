using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Agenty.RAG.IO
{
    public static class DocumentLoader
    {
        public static async Task<IReadOnlyList<(string Doc, string Source)>> LoadFilesAsync(IEnumerable<string> paths)
        {
            var docs = new List<(string, string)>();
            foreach (var p in paths)
            {
                try
                {
                    var content = await File.ReadAllTextAsync(p);
                    docs.Add((content, Path.GetFileName(p)));
                }
                catch { /* skip unreadable files */ }
            }
            return docs;
        }

        public static async Task<IReadOnlyList<(string Doc, string Source)>> LoadDirectoryAsync(string directoryPath, string searchPattern = "*.*", bool recursive = true)
        {
            if (!Directory.Exists(directoryPath)) return Array.Empty<(string, string)>();
            var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var files = Directory.EnumerateFiles(directoryPath, searchPattern, option);
            return await LoadFilesAsync(files);
        }

        public static async Task<IReadOnlyList<(string Doc, string Source)>> LoadUrlsAsync(IEnumerable<string> urls, int maxConcurrency = 6)
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; DocumentLoader/1.0)");
            var sem = new System.Threading.SemaphoreSlim(maxConcurrency);
            var docs = new List<(string, string)>();

            var tasks = urls.Select(async url =>
            {
                await sem.WaitAsync();
                try
                {
                    var resp = await http.GetAsync(url);
                    if (!resp.IsSuccessStatusCode) return;

                    if (resp.Content.Headers.ContentLength is long len && len > 5_000_000) return;

                    var html = await resp.Content.ReadAsStringAsync();
                    var text = ExtractPlainTextFromHtml(html);
                    if (!string.IsNullOrWhiteSpace(text))
                        lock (docs) docs.Add((text, url));
                }
                catch { /* skip errors */ }
                finally { sem.Release(); }
            });

            await Task.WhenAll(tasks);
            return docs;
        }

        public static string ExtractPlainTextFromHtml(string html)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var remove = doc.DocumentNode.SelectNodes("//script|//style|//nav|//footer|//form|//noscript|//aside") ?? Enumerable.Empty<HtmlNode>();
            foreach (var node in remove.ToList()) node.Remove();

            var sb = new StringBuilder();

            var infobox = doc.DocumentNode.SelectSingleNode("//table[contains(@class,'infobox')]");
            if (infobox != null)
            {
                foreach (var row in infobox.SelectNodes(".//tr") ?? Enumerable.Empty<HtmlNode>())
                {
                    var header = row.SelectSingleNode("./th")?.InnerText?.Trim();
                    var value = row.SelectSingleNode("./td")?.InnerText?.Trim();
                    if (!string.IsNullOrEmpty(header) && !string.IsNullOrEmpty(value))
                        sb.AppendLine($"{CleanText(header)}: {CleanText(value)}");
                }
            }

            var mainNodes = doc.DocumentNode.SelectNodes("//p|//h1|//h2|//h3|//li") ?? Enumerable.Empty<HtmlNode>();
            foreach (var node in mainNodes)
            {
                var text = CleanText(node.InnerText);
                if (!string.IsNullOrWhiteSpace(text))
                    sb.AppendLine(text);
            }

            return sb.ToString().Trim();
        }

        private static string CleanText(string text) =>
            text.Replace("\n", " ").Replace("\r", " ").Replace("\t", " ").Trim();
    }
}
