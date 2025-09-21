using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Agenty.RAG.IO
{
    /// <summary>
    /// Contract for any document loader (file, directory, URL, DB, etc.)
    /// </summary>
    public interface IDocumentLoader
    {
        Task<IReadOnlyList<(string Doc, string Source)>> LoadAsync(string input);
    }

    /// <summary>
    /// Load a single file from disk.
    /// </summary>
    public sealed class FileLoader : IDocumentLoader
    {
        public async Task<IReadOnlyList<(string Doc, string Source)>> LoadAsync(string path)
        {
            if (!File.Exists(path)) return Array.Empty<(string, string)>();
            try
            {
                var text = await File.ReadAllTextAsync(path);
                return new[] { (text, Path.GetFileName(path)) };
            }
            catch { return Array.Empty<(string, string)>(); }
        }
    }

    /// <summary>
    /// Load all files from a directory.
    /// </summary>
    public sealed class DirectoryLoader : IDocumentLoader
    {
        private readonly string _searchPattern;
        private readonly bool _recursive;

        public DirectoryLoader(string searchPattern = "*.*", bool recursive = true)
        {
            _searchPattern = searchPattern;
            _recursive = recursive;
        }

        public async Task<IReadOnlyList<(string Doc, string Source)>> LoadAsync(string directoryPath)
        {
            if (!Directory.Exists(directoryPath)) return Array.Empty<(string, string)>();
            var option = _recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var files = Directory.EnumerateFiles(directoryPath, _searchPattern, option);
            var loader = new FileLoader();
            var results = new List<(string, string)>();
            foreach (var f in files)
                results.AddRange(await loader.LoadAsync(f));
            return results;
        }
    }

    /// <summary>
    /// Load plain text content from a web page.
    /// </summary>
    public sealed class UrlLoader : IDocumentLoader
    {
        private readonly int _maxContentLength;

        public UrlLoader(int maxContentLength = 5_000_000)
        {
            _maxContentLength = maxContentLength;
        }

        public async Task<IReadOnlyList<(string Doc, string Source)>> LoadAsync(string url)
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; DocumentLoader/1.0)");
            try
            {
                var resp = await http.GetAsync(url);
                if (!resp.IsSuccessStatusCode) return Array.Empty<(string, string)>();

                if (resp.Content.Headers.ContentLength is long len && len > _maxContentLength)
                    return Array.Empty<(string, string)>();

                var html = await resp.Content.ReadAsStringAsync();
                var text = ExtractPlainTextFromHtml(html);
                return string.IsNullOrWhiteSpace(text) ? Array.Empty<(string, string)>() : new[] { (text, url) };
            }
            catch { return Array.Empty<(string, string)>(); }
        }

        private string ExtractPlainTextFromHtml(string html)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var remove = doc.DocumentNode.SelectNodes("//script|//style|//nav|//footer|//form|//noscript|//aside")
                         ?? Enumerable.Empty<HtmlNode>();
            foreach (var node in remove.ToList()) node.Remove();

            var sb = new StringBuilder();

            // Optional: extract infobox tables (common on Wikipedia)
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
