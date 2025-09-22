using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Agenty.RAG.Stores
{
    public sealed class FileVectorStore : IVectorStore
    {
        private readonly ConcurrentDictionary<string, VectorRecord> _store = new();
        private readonly string _persistPath;
        private readonly ILogger? _logger;

        public FileVectorStore(
        string? persistDir = null,
        string fileName = "kb.json",
        ILogger? logger = null)
        {
            _logger = logger;

            var baseDir = persistDir ??
                          Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Agenty");
            Directory.CreateDirectory(baseDir);

            _persistPath = Path.Combine(baseDir, fileName);

            Load();
        }

        public Task AddAsync(VectorRecord item)
        {
            var id = string.IsNullOrWhiteSpace(item.Id) ? RAGHelper.ComputeId(item.Content) : item.Id;
            _store.TryAdd(id, item with { Id = id });
            Save();
            return Task.CompletedTask;
        }

        public async Task AddBatchAsync(IEnumerable<VectorRecord> items)
        {
            foreach (var item in items)
            {
                var id = string.IsNullOrWhiteSpace(item.Id) ? RAGHelper.ComputeId(item.Content) : item.Id;
                _store.TryAdd(id, item with { Id = id });
            }
            Save();
            await Task.CompletedTask;
        }

        public Task<IReadOnlyList<SearchResult>> SearchAsync(float[] queryVector, int topK = 3)
        {
            var results = _store.Values
                .Select(e => new SearchResult(
                    e.Id,
                    e.Content,
                    e.Source,
                    RAGHelper.CosineSimilarity(queryVector, e.Vector)))
                .OrderByDescending(r => r.Score)
                .Take(topK)
                .ToList();

            return Task.FromResult<IReadOnlyList<SearchResult>>(results);
        }

        public bool Contains(string id) =>
            !string.IsNullOrWhiteSpace(id) && _store.ContainsKey(id);

        // === Persistence ===
        private void Save()
        {
            try
            {
                var list = _store.Values.ToList();
                var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_persistPath, json);
                _logger?.LogInformation($"[RAG] Saved {_store.Count} records to {_persistPath}");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[RAG] Failed to save store: {ex.Message}");
            }
        }

        private void Load()
        {
            if (!File.Exists(_persistPath)) return;

            try
            {
                var json = File.ReadAllText(_persistPath);
                var list = JsonSerializer.Deserialize<List<VectorRecord>>(json);
                if (list == null) return;

                foreach (var e in list)
                    _store.TryAdd(e.Id, e);

                _logger?.LogInformation($"[RAG] Loaded {_store.Count} records from {_persistPath}");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[RAG] Failed to load store: {ex.Message}");
            }
        }
    }
}
