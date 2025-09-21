//using Agenty.RAG;
//using Agenty.RAG.Stores;
//using System;
//using System.Collections.Generic;
//using System.ComponentModel;
//using System.Linq;
//using System.Text.RegularExpressions;
//using System.Threading.Tasks;

//namespace Agenty.LLMCore.BuiltInTools
//{
//    public static class LogTools
//    {
//        private static IRagRetriever? _coord;
//        private static int _defaultTopK = 3;
//        private static double _minScore = 0.6;

//        public static void Initialize(IRagRetriever coord, int topK = 3, double minScore = 0.6)
//        {
//            _coord = coord ?? throw new ArgumentNullException(nameof(coord));
//            _defaultTopK = topK;
//            _minScore = minScore;
//        }

//        [Description("Find log lines containing a keyword (case-insensitive).")]
//        public static IReadOnlyList<string> SearchKeyword(
//            [Description("Keyword or phrase to look for.")] string keyword,
//            [Description("Log lines to search within.")] IEnumerable<string> logs)
//        {
//            return logs
//                .Where(line => line.Contains(keyword, StringComparison.OrdinalIgnoreCase))
//                .ToList();
//        }

//        [Description("Find log lines matching a regex pattern.")]
//        public static IReadOnlyList<string> SearchRegex(
//            [Description("Regex pattern to match.")] string pattern,
//            [Description("Log lines to search within.")] IEnumerable<string> logs)
//        {
//            var regex = new Regex(pattern, RegexOptions.IgnoreCase);
//            return logs.Where(line => regex.IsMatch(line)).ToList();
//        }

//        [Description("Filter logs between two timestamps.")]
//        public static IReadOnlyList<string> FilterByTime(
//            [Description("Start timestamp (inclusive).")] DateTime from,
//            [Description("End timestamp (inclusive).")] DateTime to,
//            [Description("Log lines with timestamps.")] IEnumerable<(DateTime Timestamp, string Line)> logs)
//        {
//            return logs
//                .Where(l => l.Timestamp >= from && l.Timestamp <= to)
//                .Select(l => l.Line)
//                .ToList();
//        }

//        [Description("Search logs semantically (captures meaning, not just keywords).")]
//        public static async Task<IReadOnlyList<SearchResult>> SearchSemantic(
//            [Description("Query to search for.")] string query,
//            [Description("Log lines to analyze.")] IEnumerable<string> logs)
//        {
//            if (_coord == null)
//                throw new InvalidOperationException("LogTools not initialized.");

//            var text = string.Join("\n", logs);
//            await _coord.AddDocumentAsync(text, source: "logs", persist: false);

//            var results = await _coord.Search(query, _defaultTopK, SearchScope.Session);
//            return results.Where(r => r.Score >= _minScore).ToList();
//        }
//    }
//}
