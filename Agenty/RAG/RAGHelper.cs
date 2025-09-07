using HtmlAgilityPack;
using SharpToken;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Agenty.RAG
{
    public static class RAGHelper
    {
        public static string ExtractPlainTextFromHtml(string html)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Remove unwanted nodes
            var remove = doc.DocumentNode
                .SelectNodes("//script|//style|//nav|//footer|//form|//noscript|//aside")
                ?? Enumerable.Empty<HtmlNode>();
            foreach (var node in remove.ToList())
                node.Remove();

            var sb = new StringBuilder();

            // ✅ Include infobox key-value pairs
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

            // Keep paragraph, heading, list text
            var mainNodes = doc.DocumentNode
                .SelectNodes("//p|//h1|//h2|//h3|//li")
                ?? Enumerable.Empty<HtmlNode>();

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

        public static IEnumerable<string> SplitByParagraphs(string text)
        {
            return text.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries)
                       .Select(p => p.Trim())
                       .Where(p => !string.IsNullOrWhiteSpace(p));
        }

        public static IEnumerable<string> Chunk(string text, int maxTokens, int overlap, string model)
        {
            var encoder = GptEncoding.GetEncodingForModel(model);
            var tokens = encoder.Encode(text);

            int start = 0;
            while (start < tokens.Count)
            {
                var end = Math.Min(start + maxTokens, tokens.Count);
                var chunkTokens = tokens.GetRange(start, end - start);
                yield return encoder.Decode(chunkTokens);
                start += maxTokens - overlap;
            }
        }

        public static string AssembleContext(IEnumerable<string> chunks, int maxTokens, string model)
        {
            var encoder = GptEncoding.GetEncodingForModel(model);

            var context = new List<string>();
            int tokens = 0;

            foreach (var chunk in chunks)
            {
                int chunkTokens = encoder.Encode(chunk).Count;
                if (tokens + chunkTokens > maxTokens) break;
                context.Add(chunk);
                tokens += chunkTokens;
            }

            return context.Any() ? "Use the following context:\n\n" + string.Join("\n\n", context) : "";
        }

        public static string ComputeId(string text)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(text);
            var hash = sha.ComputeHash(bytes);
            return Convert.ToHexString(hash); // .NET 5+ / .NET Core
        }
    }
}
