using Agenty.RAG;
using Agenty.RAG.IO;
using Agenty.RAG.Stores;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace Agenty.BuiltInTools
{
    public class RAGTools
    {
        private readonly IRagRetriever _retriever;
        private readonly int _defaultTopK;
        private readonly double _minScore;
        private readonly string _defaultSource;

        public RAGTools(IRagRetriever retriever, int topK = 3, double minScore = 0.6, string defaultSource = "unknown")
        {
            _retriever = retriever ?? throw new ArgumentNullException(nameof(retriever));
            _defaultTopK = topK;
            _minScore = minScore;
            _defaultSource = defaultSource;
        }

        [Description("Search knowledge base for relevant context.")]
        public async Task<IReadOnlyList<SearchResult>> SearchKnowledgeBase(string query)
        {
            var results = await _retriever.Search(query, _defaultTopK);
            return results.Where(r => r.Score >= _minScore).ToList();
        }

        [Description("Search web for relevant information.")]
        public async Task<IReadOnlyList<SearchResult>> SearchWeb(string query)
        {
            var docs = await WebSearchLoader.SearchAsync(query, _defaultTopK);
            await _retriever.AddDocumentsAsync(docs.Select(d => new Document(d.Doc, d.Source ?? _defaultSource)));

            var results = await _retriever.Search(query, _defaultTopK);
            return results.Where(r => r.Score >= _minScore).ToList();
        }

        [Description("Search within a custom text block.")]
        public async Task<IReadOnlyList<SearchResult>> SearchInText(string query, string text)
        {
            await _retriever.AddDocumentAsync(new Document(text, _defaultSource));

            var results = await _retriever.Search(query, _defaultTopK);
            return results.Where(r => r.Score >= _minScore).ToList();
        }
    }
}
