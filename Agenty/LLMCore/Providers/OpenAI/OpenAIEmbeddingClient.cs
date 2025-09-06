using OpenAI;
using OpenAI.Embeddings;

namespace Agenty.LLMCore.Providers.OpenAI
{
    public class OpenAIEmbeddingClient : IEmbeddingClient
    {
        private readonly string _apiKey;
        private readonly string _defaultModel;

        public OpenAIEmbeddingClient(string apiKey, string defaultModel)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("API key must be provided.", nameof(apiKey));
            if (string.IsNullOrWhiteSpace(defaultModel))
                throw new ArgumentException("Default model must be provided", nameof(defaultModel));

            _apiKey = apiKey;
            _defaultModel = defaultModel;
        }

        public async Task<float[]> GetEmbeddingAsync(string input, string? model = null)
        {
            var client = new EmbeddingClient(model ?? _defaultModel, _apiKey);
            OpenAIEmbedding embedding = await client.GenerateEmbeddingAsync(input);
            return embedding.ToFloats().ToArray();
        }

        public async Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(IEnumerable<string> inputs, string? model = null)
        {
            var client = new EmbeddingClient(model ?? _defaultModel, _apiKey);
            OpenAIEmbeddingCollection collection = await client.GenerateEmbeddingsAsync(inputs.ToList());
            return collection.Select(e => e.ToFloats().ToArray()).ToList();
        }
    }

}
