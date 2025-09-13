using Agenty.RAG;
using Agenty.RAG.IO;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Agenty.LLMCore.BuiltInTools
{
    public static class RAGTools
    {
        private static IRagCoordinator? _coord;
        private static int _defaultTopK = 3;
        private static SearchScope _defaultScope = SearchScope.Both;

        public static void Initialize(IRagCoordinator coord, int topK = 3, SearchScope scope = SearchScope.Both)
        {
            _coord = coord ?? throw new ArgumentNullException(nameof(coord));
            _defaultTopK = topK;
            _defaultScope = scope;
        }

        [Description("Search the knowledge base for relevant context given a query.")]
        public static async Task<IReadOnlyList<SearchResult>> SearchKnowledgeBase(string query)
        {
            if (_coord == null)
                throw new InvalidOperationException("RAGTools not initialized.");

            return await _coord.Search(query, _defaultTopK, _defaultScope);
        }

        [Description("Search web (Wikipedia) and use retrieved docs as ephemeral session context")]
        public static async Task<IReadOnlyList<SearchResult>> SearchWeb(string query)
        {
            if (_coord == null)
                throw new InvalidOperationException("RAGTools not initialized.");

            var docs = await WebSearchLoader.SearchAsync(query, _defaultTopK);
            await _coord.AddDocumentsAsync(docs.Select(d => (d.Doc, d.Source)), persist: false);
            return await _coord.Search(query, _defaultTopK, SearchScope.Session);
        }

        [Description("Given a query and an ad-hoc text, chunk/embed the text and search it for relevant context. Used for one-off ephemeral analysis.")]
        public static async Task<IReadOnlyList<SearchResult>> SearchInText(string query, string text)
        {
            if (_coord == null)
                throw new InvalidOperationException("RAGTools not initialized.");

            await _coord.AddDocumentAsync(text, source: "ephemeral", persist: false);
            return await _coord.Search(query, _defaultTopK, SearchScope.Session);
        }

    }
}
