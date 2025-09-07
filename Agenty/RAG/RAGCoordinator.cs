// File: RagCoordinator.cs
using Agenty.LLMCore;
using Agenty.LLMCore.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Agenty.RAG
{
    public interface IRagCoordinator
    {
        // Ingest
        Task AddDocumentAsync(string doc, string source = "unknown");
        Task AddDocumentsAsync(IEnumerable<(string Doc, string Source)> docs);
        Task AddFilesAsync(IEnumerable<string> paths);
        Task AddDirectoryAsync(string directoryPath, string searchPattern = "*.*", bool recursive = true);
        Task AddUrlsAsync(IEnumerable<string> urls);

        // Search
        Task<IEnumerable<(string chunk, double score, string source)>> Search(string query, int topK = 3);
    }

    public sealed class RagCoordinator : IRagCoordinator
    {
        private readonly IEmbeddingClient _embeddings;
        private readonly ILogger? _logger;
        private readonly IVectorStore _store;

        private readonly int _chunkSize;
        private readonly int _chunkOverlap;
        private readonly string _tokenizerModel;
        private readonly string? _autoSavePath;

        public RagCoordinator(
            IEmbeddingClient embeddings,
            IVectorStore store,
            ILogger? logger = null,
            int chunkSize = 200,
            int chunkOverlap = 50,
            string tokenizerModel = "gpt-3.5-turbo",
            string? autoSavePath = null)
        {
            _embeddings = embeddings ?? throw new ArgumentNullException(nameof(embeddings));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _logger = logger;
            _chunkSize = chunkSize;
            _chunkOverlap = chunkOverlap;
            _tokenizerModel = tokenizerModel;
            _autoSavePath = autoSavePath;

            // Auto-load if file exists
            if (!string.IsNullOrWhiteSpace(_autoSavePath) && File.Exists(_autoSavePath))
            {
                if (_store.Load(_autoSavePath))
                    _logger?.Log($"[RAG] Loaded knowledge base from {_autoSavePath}");
            }
        }

        // === Ingest ===
        public async Task AddDocumentsAsync(IEnumerable<(string Doc, string Source)> docs)
        {
            var chunks = docs
                .SelectMany(d => RAGHelper.SplitByParagraphs(d.Doc)
                .SelectMany(s => RAGHelper.Chunk(s, _chunkSize, _chunkOverlap, _tokenizerModel)
                .Select(chunk => (chunk, d.Source))))
                .Distinct()
                .ToList();

            if (chunks.Count == 0) return;

            const int batchSize = 16;
            for (int i = 0; i < chunks.Count; i += batchSize)
            {
                var batch = chunks.Skip(i).Take(batchSize).ToList();
                var texts = batch.Select(x => x.chunk).ToList();

                var vectors = (await _embeddings.GetEmbeddingsAsync(texts)).ToArray();
                var items = new List<(string Id, string Text, float[] Vector, string Source)>();

                for (int j = 0; j < batch.Count; j++)
                {
                    items.Add((
                        Id: null!, // store can hash text if null
                        Text: batch[j].chunk,
                        Vector: Normalize(vectors[j]),
                        Source: batch[j].Source
                    ));
                }

                await _store.AddBatchAsync(items);
            }

            AutoSave();
        }

        public Task AddDocumentAsync(string doc, string source = "unknown") =>
            AddDocumentsAsync(new[] { (doc, source) });

        public async Task AddFilesAsync(IEnumerable<string> paths)
        {
            var docs = await Task.WhenAll(paths.Select(async p =>
                (await File.ReadAllTextAsync(p), Path.GetFileName(p))));
            await AddDocumentsAsync(docs);
        }

        public async Task AddDirectoryAsync(string directoryPath, string searchPattern = "*.*", bool recursive = true)
        {
            var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var files = Directory.EnumerateFiles(directoryPath, searchPattern, option);
            await AddFilesAsync(files);
        }

        public async Task AddUrlsAsync(IEnumerable<string> urls)
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; RAGAgent/1.0)");

            var docs = await Task.WhenAll(urls.Select(async url =>
            {
                var html = await http.GetStringAsync(url);
                return (RAGHelper.ExtractPlainTextFromHtml(html), url);
            }));
            await AddDocumentsAsync(docs);
        }

        // === Search ===
        public async Task<IEnumerable<(string chunk, double score, string source)>> Search(string query, int topK = 3)
        {
            var qVec = Normalize(await _embeddings.GetEmbeddingAsync(query));
            var results = await _store.SearchAsync(qVec, topK);
            return results.Select(r => (r.Text, r.Score, r.Source));
        }

        // === Helpers ===
        private void AutoSave()
        {
            if (!string.IsNullOrWhiteSpace(_autoSavePath))
            {
                _store.Save(_autoSavePath);
                _logger?.Log($"[RAG] Auto-saved knowledge base to {_autoSavePath}");
            }
        }

        private static float[] Normalize(float[] v)
        {
            var norm = Math.Sqrt(v.Sum(x => x * x));
            return norm == 0 ? v : v.Select(x => (float)(x / norm)).ToArray();
        }
    }
}
