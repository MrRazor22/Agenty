using Agenty.AgentCore.TokenHandling;
using Agenty.RAG.Embeddings;
using Agenty.RAG.Stores;
using Microsoft.Extensions.Logging;

namespace Agenty.RAG
{
    public interface IRagRetriever
    {
        Task AddDocumentAsync(string doc, string source = "unknown");
        Task AddDocumentsAsync(IEnumerable<(string Doc, string Source)> docs);
        Task<IReadOnlyList<SearchResult>> Search(string query, int topK = 3);
    }

    public sealed class RagRetriever : IRagRetriever
    {
        private readonly IEmbeddingClient _embeddings;
        private readonly IVectorStore _store;
        private readonly ITokenizer _tokenizer;
        private readonly ILogger? _logger;
        private readonly int _chunkSize;
        private readonly int _chunkOverlap;

        public RagRetriever(IEmbeddingClient embeddings, IVectorStore store, ITokenizer tokenizer, ILogger? logger = null,
            int chunkSize = 1000, int chunkOverlap = 200)
        {
            _embeddings = embeddings ?? throw new ArgumentNullException(nameof(embeddings));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _tokenizer = tokenizer ?? throw new ArgumentNullException(nameof(tokenizer));
            _logger = logger;
            _chunkSize = chunkSize;
            _chunkOverlap = chunkOverlap;
        }

        public async Task AddDocumentAsync(string doc, string source = "unknown") =>
            await AddDocumentsAsync(new[] { (doc, source) });

        public async Task AddDocumentsAsync(IEnumerable<(string Doc, string Source)> docs)
        {
            var allChunks = docs
                .SelectMany(d =>
                    SplitByParagraphs(d.Doc)
                        .SelectMany(s => Chunk(s, _chunkSize, _chunkOverlap)
                            .Select(chunk => new VectorRecord(
                                Id: RAGHelper.ComputeId(chunk),
                                Text: chunk,
                                Vector: Array.Empty<float>(),
                                Source: d.Source))))
                .DistinctBy(x => x.Id)
                .ToList();

            if (allChunks.Count == 0) return;

            var vectors = (await _embeddings.GetEmbeddingsAsync(allChunks.Select(x => x.Text).ToList())).ToArray();
            var items = allChunks.Select((x, i) => x with { Vector = RAGHelper.Normalize(vectors[i]) }).ToList();

            await _store.AddBatchAsync(items);
            _logger?.LogInformation($"[RAG] Added {items.Count} chunks (persistent KB).");
        }

        private IEnumerable<string> SplitByParagraphs(string text) =>
           text.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries)
               .Select(p => p.Trim())
               .Where(p => !string.IsNullOrWhiteSpace(p));

        private IEnumerable<string> Chunk(string text, int maxTokens, int overlap)
        {
            var tokens = _tokenizer.Encode(text);

            for (int start = 0; start < tokens.Count; start += maxTokens - overlap)
            {
                var end = Math.Min(start + maxTokens, tokens.Count);
                var chunkTokens = tokens.Skip(start).Take(end - start);
                yield return _tokenizer.Decode(chunkTokens);
            }
        }

        public async Task<IReadOnlyList<SearchResult>> Search(string query, int topK = 3)
        {
            var qVec = RAGHelper.Normalize(await _embeddings.GetEmbeddingAsync(query));
            return await _store.SearchAsync(qVec, topK);
        }
    }
}
