using System.Collections.Concurrent;

namespace Agenty.RAG.Stores
{
    public sealed class InMemoryVectorStore : IVectorStore
    {
        private readonly ConcurrentDictionary<string, VectorRecord> _store = new();

        public Task AddAsync(VectorRecord item)
        {
            var id = string.IsNullOrWhiteSpace(item.Id) ? RAGHelper.ComputeId(item.Content) : item.Id;
            _store.TryAdd(id, item with { Id = id });
            return Task.CompletedTask;
        }

        public Task AddBatchAsync(IEnumerable<VectorRecord> items)
        {
            foreach (var item in items)
            {
                var id = string.IsNullOrWhiteSpace(item.Id) ? RAGHelper.ComputeId(item.Content) : item.Id;
                _store.TryAdd(id, item with { Id = id });
            }
            return Task.CompletedTask;
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

        public void Clear() => _store.Clear();
    }
}
