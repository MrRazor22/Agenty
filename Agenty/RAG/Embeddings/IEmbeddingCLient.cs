using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Agenty.RAG.Embeddings
{
    public interface IEmbeddingClient
    {
        Task<float[]> GetEmbeddingAsync(string input, string? model = null);
        Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(IEnumerable<string> inputs, string? model = null);
    }
}
