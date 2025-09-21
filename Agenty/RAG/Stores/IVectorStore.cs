using System.Collections.Concurrent;
using System.Text.Json;
using Agenty.LLMCore.Logging;
using Microsoft.Extensions.Logging;

namespace Agenty.RAG.Stores
{
    public record VectorRecord(
        string Id,
        string Text,
        float[] Vector,
        string Source
    );

    public record SearchResult(
        string Id,
        string Text,
        string Source,
        double Score
    );

    public interface IVectorStore
    {
        Task AddAsync(VectorRecord item);
        Task AddBatchAsync(IEnumerable<VectorRecord> items);
        Task<IReadOnlyList<SearchResult>> SearchAsync(float[] queryVector, int topK = 3);
        bool Contains(string id);
    }
}
