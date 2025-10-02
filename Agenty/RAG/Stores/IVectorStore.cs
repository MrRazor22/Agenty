using System.Collections.Generic;
using System.Threading.Tasks;

namespace Agenty.RAG.Stores
{
    public class VectorRecord
    {
        public string Id { get; }
        public string Content { get; }
        public float[] Vector { get; }
        public string Source { get; }

        public VectorRecord(string id, string content, float[] vector, string source)
        {
            Id = id;
            Content = content;
            Vector = vector;
            Source = source;
        }

        // manual clone helper
        public VectorRecord With(string id = null, string content = null, float[] vector = null, string source = null)
        {
            return new VectorRecord(
                id ?? this.Id,
                content ?? this.Content,
                vector ?? this.Vector,
                source ?? this.Source
            );
        }
    }


    public class SearchResult
    {
        public string Id { get; }
        public string Content { get; }
        public string Source { get; }
        public double Score { get; }

        public SearchResult(string id, string content, string source, double score)
        {
            Id = id;
            Content = content;
            Source = source;
            Score = score;
        }
    }

    public interface IVectorStore
    {
        Task AddAsync(VectorRecord item);
        Task AddBatchAsync(IEnumerable<VectorRecord> items);
        Task<IReadOnlyList<SearchResult>> SearchAsync(float[] queryVector, int topK = 3);
        bool Contains(string id);
    }
}
