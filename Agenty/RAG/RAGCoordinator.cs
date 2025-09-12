// File: RagCoordinator.cs
using Agenty.AgentCore;
using Agenty.LLMCore;
using Agenty.LLMCore.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Agenty.RAG
{
    public interface IRagCoordinator
    {
        Task AddDocumentAsync(string doc, string source = "unknown");
        Task AddDocumentsAsync(IEnumerable<(string Doc, string Source)> docs);
        Task<IReadOnlyList<SearchResult>> Search(string query, int topK = 3);
    }

    /// <summary>
    /// Orchestrates document ingestion (chunk → embed → store) and search.
    /// Persistence is owned by the VectorStore, not by this class.
    /// </summary>
    public sealed class RagCoordinator : IRagCoordinator
    {
        private readonly IEmbeddingClient _embeddings;
        private readonly IVectorStore _store;
        private readonly ITokenizer _tokenizer;
        private readonly ILogger? _logger;

        private readonly int _chunkSize;
        private readonly int _chunkOverlap;

        public RagCoordinator(
            IEmbeddingClient embeddings,
            IVectorStore store,
            ITokenizer tokenizer,
            ILogger? logger = null,
            int chunkSize = 1000,
            int chunkOverlap = 200)
        {
            _embeddings = embeddings ?? throw new ArgumentNullException(nameof(embeddings));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _tokenizer = tokenizer ?? throw new ArgumentNullException(nameof(tokenizer));
            _logger = logger;
            _chunkSize = chunkSize;
            _chunkOverlap = chunkOverlap;
        }

        private static bool ShouldSkipSource(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return false;
            var fileName = Path.GetFileName(source);
            if (fileName.Equals("kb.json", StringComparison.OrdinalIgnoreCase)) return true;
            if (fileName.StartsWith("~") || fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public async Task AddDocumentAsync(string doc, string source = "unknown") =>
            await AddDocumentsAsync(new[] { (doc, source) });

        public async Task AddDocumentsAsync(IEnumerable<(string Doc, string Source)> docs)
        {
            var allChunks = docs
                .Where(d => !ShouldSkipSource(d.Source))
                .SelectMany(d =>
                    RAGHelper.SplitByParagraphs(d.Doc)
                        .SelectMany(s => RAGHelper.Chunk(s, _chunkSize, _chunkOverlap, _tokenizer)
                            .Select(chunk => new VectorRecord(
                                Id: RAGHelper.ComputeId(chunk),
                                Text: chunk,
                                Vector: Array.Empty<float>(), // placeholder
                                Source: d.Source))))
                .DistinctBy(x => x.Id)
                .ToList();

            if (allChunks.Count == 0)
            {
                _logger?.Log("[RAG] No chunks produced.");
                return;
            }

            var newChunks = allChunks.Where(c => !_store.Contains(c.Id)).ToList();
            if (newChunks.Count == 0)
            {
                _logger?.Log("[RAG] No new chunks to add (all already present).");
                return;
            }

            _logger?.Log($"[RAG] {allChunks.Count} total chunks, {newChunks.Count} new → embedding only new chunks.");

            const int batchSize = 16;
            for (int i = 0; i < newChunks.Count; i += batchSize)
            {
                var batch = newChunks.Skip(i).Take(batchSize).ToList();
                var texts = batch.Select(x => x.Text).ToList();

                var vectors = (await _embeddings.GetEmbeddingsAsync(texts)).ToArray();
                var items = batch.Select((x, j) => x with { Vector = Normalize(vectors[j]) });

                await _store.AddBatchAsync(items);
            }

            _logger?.Log("[RAG] Ingestion complete.");
        }

        public async Task<IReadOnlyList<SearchResult>> Search(string query, int topK = 3)
        {
            var qVec = Normalize(await _embeddings.GetEmbeddingAsync(query));
            return await _store.SearchAsync(qVec, topK);
        }

        private static float[] Normalize(float[] v)
        {
            var norm = Math.Sqrt(v.Sum(x => x * x));
            return norm == 0 ? v : v.Select(x => (float)(x / norm)).ToArray();
        }
    }
}
