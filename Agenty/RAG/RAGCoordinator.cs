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
    public enum SearchScope
    {
        Both,           // Search both persistent and ephemeral (default)
        KnowledgeBase,  // Search only persistent store
        Session         // Search only ephemeral/session docs
    }

    public interface IRagCoordinator
    {
        Task AddDocumentAsync(string doc, string source = "unknown", bool persist = true);
        Task AddDocumentsAsync(IEnumerable<(string Doc, string Source)> docs, bool persist = true);
        Task<IReadOnlyList<SearchResult>> Search(string query, int topK = 3, SearchScope scope = SearchScope.Both);
    }

    /// <summary>
    /// Orchestrates document ingestion (chunk → embed → store) and search.
    /// Supports both persistent (knowledge base) and ephemeral (session-only) documents.
    /// </summary>
    public sealed class RagCoordinator : IRagCoordinator
    {
        private readonly IEmbeddingClient _embeddings;
        private readonly IVectorStore _store;
        private readonly ITokenizer _tokenizer;
        private readonly ILogger? _logger;

        private readonly int _chunkSize;
        private readonly int _chunkOverlap;
        private readonly List<VectorRecord> _ephemeral = new();

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

        public async Task AddDocumentAsync(string doc, string source = "unknown", bool persist = true) =>
            await AddDocumentsAsync(new[] { (doc, source) }, persist);

        public async Task AddDocumentsAsync(IEnumerable<(string Doc, string Source)> docs, bool persist = true)
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

            List<VectorRecord> newChunks;
            if (persist)
            {
                // For persistent docs, check against store to avoid duplicates
                newChunks = allChunks.Where(c => !_store.Contains(c.Id)).ToList();
                if (newChunks.Count == 0)
                {
                    _logger?.Log("[RAG] No new chunks to add (all already present in persistent store).");
                    return;
                }
            }
            else
            {
                // For ephemeral docs, check against ephemeral list to avoid duplicates
                lock (_ephemeral)
                {
                    var existingIds = _ephemeral.Select(x => x.Id).ToHashSet();
                    newChunks = allChunks.Where(c => !existingIds.Contains(c.Id)).ToList();
                }
                if (newChunks.Count == 0)
                {
                    _logger?.Log("[RAG] No new chunks to add (all already present in ephemeral store).");
                    return;
                }
            }

            _logger?.Log($"[RAG] {allChunks.Count} total chunks, {newChunks.Count} new → embedding only new chunks.");

            const int batchSize = 16;
            for (int i = 0; i < newChunks.Count; i += batchSize)
            {
                var batch = newChunks.Skip(i).Take(batchSize).ToList();
                var texts = batch.Select(x => x.Text).ToList();

                var vectors = (await _embeddings.GetEmbeddingsAsync(texts)).ToArray();
                var items = batch.Select((x, j) => x with { Vector = RAGHelper.Normalize(vectors[j]) });

                if (persist)
                {
                    await _store.AddBatchAsync(items);
                }
                else
                {
                    lock (_ephemeral)
                    {
                        _ephemeral.AddRange(items);
                    }
                }
            }

            var storeType = persist ? "persistent" : "ephemeral";
            _logger?.Log($"[RAG] Ingestion complete ({newChunks.Count} chunks added to {storeType} store).");
        }

        public async Task<IReadOnlyList<SearchResult>> Search(string query, int topK = 3, SearchScope scope = SearchScope.Both)
        {
            var qVec = RAGHelper.Normalize(await _embeddings.GetEmbeddingAsync(query));

            return scope switch
            {
                SearchScope.KnowledgeBase => await _store.SearchAsync(qVec, topK),
                SearchScope.Session => SearchEphemeral(qVec, topK),
                SearchScope.Both => await SearchBoth(qVec, topK),
                _ => throw new ArgumentOutOfRangeException(nameof(scope))
            };
        }

        private IReadOnlyList<SearchResult> SearchEphemeral(float[] queryVector, int topK)
        {
            lock (_ephemeral)
            {
                if (_ephemeral.Count == 0)
                    return Array.Empty<SearchResult>();

                var results = _ephemeral
                    .Select(record => new SearchResult(
                        record.Id,
                        record.Text,
                        record.Source,
                        RAGHelper.CosineSimilarity(queryVector, record.Vector)))
                    .OrderByDescending(r => r.Score)
                    .Take(topK)
                    .ToList();

                return results.AsReadOnly();
            }
        }

        private async Task<IReadOnlyList<SearchResult>> SearchBoth(float[] queryVector, int topK)
        {
            // Get results from both stores
            var persistentResults = await _store.SearchAsync(queryVector, topK * 2); // Get more to ensure good ranking
            var ephemeralResults = SearchEphemeral(queryVector, topK * 2);

            // Merge and re-rank
            var allResults = persistentResults.Concat(ephemeralResults)
                .OrderByDescending(r => r.Score)
                .Take(topK)
                .ToList();

            return allResults.AsReadOnly();
        }
    }
}