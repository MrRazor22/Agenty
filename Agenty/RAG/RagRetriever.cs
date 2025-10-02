using Agenty.AgentCore.TokenHandling;
using Agenty.RAG.Embeddings;
using Agenty.RAG.IO;
using Agenty.RAG.Stores;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Agenty.RAG
{
    public interface IRagRetriever
    {
        Task AddDocumentAsync(Document doc);
        Task AddDocumentsAsync(IEnumerable<Document> docs, int batchSize = 32, int maxParallel = 2);
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
            int chunkSize = 400, int chunkOverlap = 100)
        {
            _embeddings = embeddings ?? throw new ArgumentNullException(nameof(embeddings));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _tokenizer = tokenizer ?? throw new ArgumentNullException(nameof(tokenizer));
            _logger = logger;
            _chunkSize = chunkSize;
            _chunkOverlap = chunkOverlap;
        }

        public async Task AddDocumentAsync(Document doc) => await AddDocumentsAsync(new[] { doc });

        public async Task AddDocumentsAsync(
            IEnumerable<Document> docs,
            int batchSize = 32,
            int maxParallel = 2)
        {
            var allChunks = docs
    .SelectMany(d =>
        SplitByParagraphs(d.Content)
            .SelectMany(s => Chunk(s, _chunkSize, _chunkOverlap)
                .Select(chunk => new VectorRecord(
                    RAGHelper.ComputeId(chunk),
                    chunk,
                    Array.Empty<float>(),
                    d.Source))))
                .GroupBy(x => x.Id)
                .Select(g => g.First())
                .ToList();

            if (allChunks.Count == 0)
            {
                _logger?.LogInformation("[RAG] No chunks produced from input documents.");
                return;
            }

            _logger?.LogInformation($"[RAG] {allChunks.Count} total unique chunks produced.");

            // Filter out already-present chunks
            var newChunks = allChunks.Where(c => !_store.Contains(c.Id)).ToList();
            if (newChunks.Count == 0)
            {
                _logger?.LogInformation("[RAG] All chunks already exist in the store. Nothing new to add.");
                return;
            }

            _logger?.LogInformation($"[RAG] {newChunks.Count} new chunks to embed and add to store.");


            var batches = newChunks
                .Select((c, i) => new { c, i })
                .GroupBy(x => x.i / batchSize)
                .Select(g => g.Select(x => x.c).ToList())
                .ToList();

            _logger?.LogInformation($"[RAG] Processing {batches.Count} batches (batchSize={batchSize}, concurrency={maxParallel}).");

            var semaphore = new SemaphoreSlim(maxParallel);
            var tasks = batches.Select(async (batch, batchIndex) =>
            {
                await semaphore.WaitAsync();
                try
                {
                    _logger?.LogDebug($"[RAG] Embedding batch {batchIndex + 1}/{batches.Count} with {batch.Count} chunks...");
                    var texts = batch.Select(x => x.Content).ToList();

                    var vectors = (await _embeddings.GetEmbeddingsAsync(texts)).ToArray();
                    var items = batch
                                .Select((x, j) => x.With(vector: RAGHelper.Normalize(vectors[j])))
                                .ToList();
                    _logger?.LogDebug($"[RAG] Completed embeddings for batch {batchIndex + 1}.");
                    return items;
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var results = await Task.WhenAll(tasks);
            var itemsToAdd = results.SelectMany(r => r).ToList();

            await _store.AddBatchAsync(itemsToAdd);
            _logger?.LogInformation($"[RAG] Ingestion complete. {itemsToAdd.Count} chunks added to persistent store.");
        }

        private IEnumerable<string> SplitByParagraphs(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                yield break;

            // Normalize line endings
            var normalized = text.Replace("\r\n", "\n").Replace("\r", "\n");

            // Split on 2+ newlines
            var parts = Regex.Split(normalized, @"\n{2,}");

            foreach (var part in parts)
            {
                if (!string.IsNullOrWhiteSpace(part))
                    yield return part;
            }
        }

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
