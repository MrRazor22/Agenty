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
    public record RetrievedChunk(string Id, string Text, double Score, string Source);
    public record RagContext(string Context, IReadOnlyList<RetrievedChunk> Chunks);

    public interface IRagCoordinator
    {
        // Ingest
        Task AddDocumentAsync(string doc, string source = "unknown");
        Task AddDocumentsAsync(IEnumerable<(string Doc, string Source)> docs);
        Task AddFilesAsync(IEnumerable<string> paths);
        Task AddDirectoryAsync(string directoryPath, string searchPattern = "*.*", bool recursive = true);
        Task AddUrlsAsync(IEnumerable<string> urls, int maxConcurrency = 6);

        // Search
        Task<IReadOnlyList<RetrievedChunk>> Search(string query, int topK = 3);
        Task<RagContext> GetContext(string query, int topK = 3, int maxTokens = 2000);
    }

    public sealed class RagCoordinator : IRagCoordinator
    {
        private readonly IEmbeddingClient _embeddings;
        private readonly ILogger? _logger;
        private readonly IVectorStore _store;

        private readonly int _chunkSize;
        private readonly int _chunkOverlap;
        private readonly string _tokenizerModel;
        private readonly string? _autoSaveFile;

        public RagCoordinator(
            IEmbeddingClient embeddings,
            IVectorStore store,
            ILogger? logger = null,
            int chunkSize = 1000,
            int chunkOverlap = 200,
            string tokenizerModel = "gpt-3.5-turbo",
            string? autoSaveDir = null)
        {
            _embeddings = embeddings ?? throw new ArgumentNullException(nameof(embeddings));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _logger = logger;
            _chunkSize = chunkSize;
            _chunkOverlap = chunkOverlap;
            _tokenizerModel = tokenizerModel;

            if (!string.IsNullOrWhiteSpace(autoSaveDir))
            {
                Directory.CreateDirectory(autoSaveDir); // ensure exists
                _autoSaveFile = Path.Combine(autoSaveDir, "kb.json");

                if (File.Exists(_autoSaveFile) && _store.Load(_autoSaveFile))
                    _logger?.Log($"[RAG] Loaded knowledge base from {_autoSaveFile}");
            }
        }
        private bool ShouldSkipSource(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return false;

            var fileName = Path.GetFileName(source);
            if (fileName.Equals("kb.json", StringComparison.OrdinalIgnoreCase)) return true;

            if (fileName.StartsWith("~") || fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        // === Ingest ===
        public async Task AddDocumentsAsync(IEnumerable<(string Doc, string Source)> docs)
        {
            var allChunks = docs
                .Where(d => !ShouldSkipSource(d.Source))
                .SelectMany(d => RAGHelper.SplitByParagraphs(d.Doc)
                    .SelectMany(s => RAGHelper.Chunk(s, _chunkSize, _chunkOverlap, _tokenizerModel)
                    .Select(chunk => (Id: RAGHelper.ComputeId(chunk), Text: chunk, Source: d.Source))))
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

            _logger?.Log($"[RAG] {allChunks.Count} total chunks, {newChunks.Count} new -> embedding only new chunks.");

            const int batchSize = 16;
            for (int i = 0; i < newChunks.Count; i += batchSize)
            {
                var batch = newChunks.Skip(i).Take(batchSize).ToList();
                var texts = batch.Select(x => x.Text).ToList();

                var vectors = (await _embeddings.GetEmbeddingsAsync(texts)).ToArray();
                var items = batch.Select((x, j) => (
                    Id: x.Id,
                    Text: x.Text,
                    Vector: Normalize(vectors[j]),
                    Source: x.Source
                ));

                await _store.AddBatchAsync(items);
            }

            AutoSave();
            _logger?.Log("[RAG] Ingestion complete.");
        }

        public Task AddDocumentAsync(string doc, string source = "unknown") =>
            AddDocumentsAsync(new[] { (doc, source) });

        public async Task AddFilesAsync(IEnumerable<string> paths)
        {
            var docs = new List<(string Doc, string Source)>();
            foreach (var p in paths)
            {
                try
                {
                    var content = await File.ReadAllTextAsync(p);
                    docs.Add((content, Path.GetFileName(p)));
                }
                catch (Exception ex)
                {
                    _logger?.Log($"[RAG] Could not read file '{p}': {ex.Message}");
                }
            }

            if (docs.Count > 0)
                await AddDocumentsAsync(docs);
        }

        public async Task AddDirectoryAsync(string directoryPath, string searchPattern = "*.*", bool recursive = true)
        {
            if (!Directory.Exists(directoryPath))
            {
                _logger?.Log($"[RAG] Directory not found: {directoryPath}");
                return;
            }

            var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var files = Directory.EnumerateFiles(directoryPath, searchPattern, option);
            await AddFilesAsync(files);
        }

        public async Task AddUrlsAsync(IEnumerable<string> urls, int maxConcurrency = 6)
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; RAGAgent/1.0)");
            var sem = new System.Threading.SemaphoreSlim(maxConcurrency);
            var docs = new List<(string Doc, string Source)>();

            var tasks = urls.Select(async url =>
            {
                await sem.WaitAsync();
                try
                {
                    HttpResponseMessage? resp;
                    try
                    {
                        resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                    }
                    catch (HttpRequestException hre)
                    {
                        _logger?.Log($"[RAG] HTTP error for {url}: {hre.Message}");
                        return;
                    }

                    if (!resp.IsSuccessStatusCode)
                    {
                        _logger?.Log($"[RAG] Skipping {url}: HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}");
                        return;
                    }

                    if (resp.Content.Headers.ContentLength.HasValue &&
                        resp.Content.Headers.ContentLength.Value > 5_000_000)
                    {
                        _logger?.Log($"[RAG] Skipping large page {url} ({resp.Content.Headers.ContentLength} bytes)");
                        return;
                    }

                    var html = await resp.Content.ReadAsStringAsync();
                    var text = RAGHelper.ExtractPlainTextFromHtml(html);
                    if (!string.IsNullOrWhiteSpace(text))
                        lock (docs) { docs.Add((text, url)); }
                }
                catch (Exception ex)
                {
                    _logger?.Log($"[RAG] Error fetching {url}: {ex.Message}");
                }
                finally { sem.Release(); }
            });

            await Task.WhenAll(tasks);

            if (docs.Count > 0)
                await AddDocumentsAsync(docs);
        }

        public async Task<IReadOnlyList<RetrievedChunk>> Search(string query, int topK = 3)
        {
            var qVec = Normalize(await _embeddings.GetEmbeddingAsync(query));
            var results = await _store.SearchAsync(qVec, topK);

            return results.Select(r => new RetrievedChunk(
                Id: r.Id,
                Text: r.Text,
                Score: r.Score,
                Source: r.Source
            )).ToList();
        }

        public async Task<RagContext> GetContext(string query, int topK = 3, int maxTokens = 2000)
        {
            var retrieved = (await Search(query, topK)).ToList();

            if (!retrieved.Any())
                retrieved = (await Search(query, topK * 2)).ToList();

            var contextChunks = retrieved.Select(r => $"[{r.Source}] {r.Text}");
            string context = RAGHelper.AssembleContext(contextChunks, maxTokens, _tokenizerModel);

            return new RagContext(context, retrieved);
        }

        // === Helpers ===
        private void AutoSave()
        {
            if (!string.IsNullOrWhiteSpace(_autoSaveFile))
            {
                _store.Save(_autoSaveFile);
                _logger?.Log($"[RAG] Auto-saved knowledge base to {_autoSaveFile}");
            }
        }

        private static float[] Normalize(float[] v)
        {
            var norm = Math.Sqrt(v.Sum(x => x * x));
            return norm == 0 ? v : v.Select(x => (float)(x / norm)).ToArray();
        }
    }
}
