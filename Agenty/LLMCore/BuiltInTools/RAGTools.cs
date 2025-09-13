using Agenty.RAG;
using Agenty.RAG.IO;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace Agenty.LLMCore.BuiltInTools
{
    public class RAGTools
    {
        private IRagCoordinator? _coord;
        private int _defaultTopK = 3;
        private SearchScope _defaultScope = SearchScope.Both;
        private double _minScore = 0.6;

        public RAGTools(IRagCoordinator coord, int topK = 3, SearchScope scope = SearchScope.Both, double minScore = 0.6)
        {
            _coord = coord;
            _defaultTopK = topK;
            _defaultScope = scope;
            _minScore = minScore;
        }

        [Description("Search knowledge base for relevant context.")]
        public async Task<IReadOnlyList<SearchResult>> SearchKnowledgeBase(
            [Description("Query to search for.")] string query)
        {
            if (_coord == null)
                throw new InvalidOperationException("RAGTools not initialized.");

            var results = await _coord.Search(query, _defaultTopK, _defaultScope);
            return results.Where(r => r.Score >= _minScore).ToList();
        }

        [Description("Search Wikipedia for relevant information.")]
        public async Task<IReadOnlyList<SearchResult>> SearchWeb(
            [Description("Query to search online.")] string query)
        {
            if (_coord == null)
                throw new InvalidOperationException("RAGTools not initialized.");

            var docs = await WebSearchLoader.SearchAsync(query, _defaultTopK);
            await _coord.AddDocumentsAsync(docs.Select(d => (d.Doc, d.Source)), persist: false);

            var results = await _coord.Search(query, _defaultTopK, SearchScope.Session);
            return results.Where(r => r.Score >= _minScore).ToList();
        }

        [Description("Search within a custom text block.")]
        public async Task<IReadOnlyList<SearchResult>> SearchInText(
            [Description("Query to search for.")] string query,
            [Description("The text content to analyze.")] string text)
        {
            if (_coord == null)
                throw new InvalidOperationException("RAGTools not initialized.");

            await _coord.AddDocumentAsync(text, source: "ephemeral", persist: false);

            var results = await _coord.Search(query, _defaultTopK, SearchScope.Session);
            return results.Where(r => r.Score >= _minScore).ToList();
        }
    }
}
