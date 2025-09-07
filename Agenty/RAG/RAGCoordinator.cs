using Agenty.RAG;
using Agenty.LLMCore;
using Agenty.LLMCore.Logging;
using HtmlAgilityPack;
using SharpToken;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
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
        Task<IEnumerable<(string chunk, double score, string source)>> SearchAsync(string query, int topK = 3);

        // Persistence
        void SaveKnowledgeBase(string path);
        void LoadKnowledgeBase(string path);
    }
    public sealed class RagCoordinator : IRagCoordinator
    {
        private readonly IEmbeddingClient _embeddings;
        private readonly ILogger? _logger;
        private readonly List<KBEntry> _knowledgeBase = new();

        private readonly int _chunkSize;
        private readonly int _chunkOverlap;
        private readonly string _tokenizerModel;
        private readonly double _similarityThreshold;

        public record KBEntry(string Id, string Source, string Text, float[] Vector, DateTime Added);

        public RagCoordinator(
            IEmbeddingClient embeddings,
            ILogger? logger = null,
            int chunkSize = 200,
            int chunkOverlap = 50,
            string tokenizerModel = "gpt-3.5-turbo",
            double similarityThreshold = 0.3)
        {
            _embeddings = embeddings ?? throw new ArgumentNullException(nameof(embeddings));
            _logger = logger;
            _chunkSize = chunkSize;
            _chunkOverlap = chunkOverlap;
            _tokenizerModel = tokenizerModel;
            _similarityThreshold = similarityThreshold;
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
                for (int j = 0; j < batch.Count; j++)
                {
                    _knowledgeBase.Add(new KBEntry(
                        Guid.NewGuid().ToString(),
                        batch[j].Source,
                        batch[j].chunk,
                        Normalize(vectors[j]),
                        DateTime.UtcNow
                    ));
                }
            }
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
        public async Task<IEnumerable<(string chunk, double score, string source)>> SearchAsync(string query, int topK = 3)
        {
            if (!_knowledgeBase.Any()) return Enumerable.Empty<(string, double, string)>();

            var qVec = Normalize(await _embeddings.GetEmbeddingAsync(query));
            return _knowledgeBase
                .Select(k => (k.Text, RAGHelper.CosineSimilarity(qVec, k.Vector), k.Source))
                .Where(x => x.Item2 >= _similarityThreshold)
                .OrderByDescending(x => x.Item2)
                .Take(topK)
                .ToList();
        }

        // === Persistence ===
        public void SaveKnowledgeBase(string path) =>
            File.WriteAllText(path, JsonSerializer.Serialize(_knowledgeBase));

        public void LoadKnowledgeBase(string path)
        {
            if (!File.Exists(path)) return;
            var list = JsonSerializer.Deserialize<List<KBEntry>>(File.ReadAllText(path));
            if (list != null) _knowledgeBase.AddRange(list);
        }

        // === Helpers ===
        private static float[] Normalize(float[] v)
        {
            var norm = Math.Sqrt(v.Sum(x => x * x));
            return norm == 0 ? v : v.Select(x => (float)(x / norm)).ToArray();
        }
    }
}
