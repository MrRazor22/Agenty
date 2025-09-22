using Agenty.RAG;
using Agenty.RAG.IO;
using Agenty.RAG.Stores;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Agenty.BuiltInTools
{
    public class LogTools
    {
        private readonly IRagRetriever _retriever;
        private readonly int _defaultTopK;
        private readonly double _minScore;
        private readonly string _defaultSource;

        public LogTools(IRagRetriever retriever, int topK = 3, double minScore = 0.6, string defaultSource = "logs")
        {
            _retriever = retriever ?? throw new ArgumentNullException(nameof(retriever));
            _defaultTopK = topK;
            _minScore = minScore;
            _defaultSource = defaultSource;
        }

        [Description("Find log lines containing a keyword (case-insensitive).")]
        public IReadOnlyList<string> SearchKeyword(
            [Description("Keyword or phrase to look for.")] string keyword,
            [Description("Log lines to search within.")] IEnumerable<string> logs)
        {
            return logs
                .Where(line => line.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        [Description("Find log lines matching a regex pattern.")]
        public IReadOnlyList<string> SearchRegex(
            [Description("Regex pattern to match.")] string pattern,
            [Description("Log lines to search within.")] IEnumerable<string> logs)
        {
            var regex = new Regex(pattern, RegexOptions.IgnoreCase);
            return logs.Where(line => regex.IsMatch(line)).ToList();
        }

        [Description("Filter logs between two timestamps.")]
        public IReadOnlyList<string> FilterByTime(
            [Description("Start timestamp (inclusive).")] DateTime from,
            [Description("End timestamp (inclusive).")] DateTime to,
            [Description("Log lines with timestamps.")] IEnumerable<(DateTime Timestamp, string Line)> logs)
        {
            return logs
                .Where(l => l.Timestamp >= from && l.Timestamp <= to)
                .Select(l => l.Line)
                .ToList();
        }

        [Description("Search logs semantically (captures meaning, not just keywords).")]
        public async Task<IReadOnlyList<SearchResult>> SearchSemantic(
            [Description("Query to search for.")] string query,
            [Description("Log lines to analyze.")] IEnumerable<string> logs)
        {
            var text = string.Join("\n", logs);
            await _retriever.AddDocumentAsync(new Document(text, _defaultSource));

            var results = await _retriever.Search(query, _defaultTopK);
            return results.Where(r => r.Score >= _minScore).ToList();
        }
    }
}
