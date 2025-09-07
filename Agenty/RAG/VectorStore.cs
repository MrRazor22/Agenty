using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Agenty.RAG
{
    public interface IVectorStore
    {
        Task AddAsync(string id, string text, float[] vector, string source);
        Task AddBatchAsync(IEnumerable<(string Id, string Text, float[] Vector, string Source)> items);
        Task<IEnumerable<(string Id, string Text, string Source, double Score)>> SearchAsync(float[] queryVector, int topK = 3);
        void Save(string path);
        bool Load(string path);
    }

    public sealed class InMemoryVectorStore : IVectorStore
    {
        private readonly ConcurrentDictionary<string, Entry> _store = new();

        private record Entry(string Id, string Text, float[] Vector, string Source);

        // === Helpers ===
        private static string ComputeId(string text)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
            return Convert.ToHexString(hash);
        }

        private static double CosineSimilarity(float[] v1, float[] v2)
        {
            if (v1.Length != v2.Length)
                throw new ArgumentException("Vector dimensions must match.");

            double dot = 0, norm1 = 0, norm2 = 0;
            for (int i = 0; i < v1.Length; i++)
            {
                dot += v1[i] * v2[i];
                norm1 += v1[i] * v1[i];
                norm2 += v2[i] * v2[i];
            }
            return (norm1 == 0 || norm2 == 0) ? 0 : dot / (Math.Sqrt(norm1) * Math.Sqrt(norm2));
        }

        // === Public API ===
        public Task AddAsync(string id, string text, float[] vector, string source)
        {
            // If user passes null/empty id → use hash of text
            id = string.IsNullOrWhiteSpace(id) ? ComputeId(text) : id;

            _store.TryAdd(id, new Entry(id, text, vector, source));
            return Task.CompletedTask;
        }

        public async Task AddBatchAsync(IEnumerable<(string Id, string Text, float[] Vector, string Source)> items)
        {
            foreach (var (id, text, vector, source) in items)
            {
                var key = string.IsNullOrWhiteSpace(id) ? ComputeId(text) : id;
                _store.TryAdd(key, new Entry(key, text, vector, source));
            }
            await Task.CompletedTask;
        }

        public Task<IEnumerable<(string Id, string Text, string Source, double Score)>> SearchAsync(float[] queryVector, int topK = 3)
        {
            var results = _store.Values
                .Select(e => (e.Id, e.Text, e.Source, Score: CosineSimilarity(queryVector, e.Vector)))
                .OrderByDescending(r => r.Score)
                .Take(topK);

            return Task.FromResult(results);
        }

        public void Save(string path)
        {
            var list = _store.Values.ToList();
            var json = JsonSerializer.Serialize(list);
            File.WriteAllText(path, json);
        }

        public bool Load(string path)
        {
            if (!File.Exists(path)) return false;

            var json = File.ReadAllText(path);
            var list = JsonSerializer.Deserialize<List<Entry>>(json);
            if (list == null) return false;

            foreach (var e in list)
                _store.TryAdd(e.Id, e);

            return true;
        }

        // === Extra convenience ===
        public bool HasData() => !_store.IsEmpty;
        public int Count => _store.Count;
    }
}
