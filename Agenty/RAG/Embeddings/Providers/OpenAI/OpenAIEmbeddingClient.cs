using OpenAI;
using OpenAI.Embeddings;
using System.ClientModel;
using System.Text.Json;

namespace Agenty.RAG.Embeddings.Providers.OpenAI
{
    public class OpenAIEmbeddingClient : IEmbeddingClient
    {
        private readonly ApiKeyCredential _credential;
        private readonly string _defaultModel;
        private readonly Uri _baseUrl;

        public OpenAIEmbeddingClient(string baseUrl, string apiKey, string defaultModel)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("API key must be provided.", nameof(apiKey));
            if (string.IsNullOrWhiteSpace(defaultModel))
                throw new ArgumentException("Default model must be provided", nameof(defaultModel));

            _credential = new ApiKeyCredential(apiKey);
            _defaultModel = defaultModel;
            _baseUrl = new Uri(baseUrl);
        }

        private EmbeddingClient CreateClient(string? model = null)
        {
            var options = new OpenAIClientOptions { Endpoint = _baseUrl };
            return new EmbeddingClient(model ?? _defaultModel, _credential, options);
        }

        public async Task<float[]> GetEmbeddingAsync(string input, string? model = null)
        {
            var client = CreateClient(model);

            // Build payload with encoding_format=float
            var payload = BinaryData.FromObjectAsJson(new
            {
                model = model ?? _defaultModel,
                input,
                encoding_format = "float"
            });

            using var content = BinaryContent.Create(payload);
            ClientResult result = await client.GenerateEmbeddingsAsync(content);
            BinaryData output = result.GetRawResponse().Content;

            using var doc = JsonDocument.Parse(output.ToString());
            var vector = doc.RootElement
                .GetProperty("data")[0]
                .GetProperty("embedding")
                .EnumerateArray()
                .Select(x => (float)x.GetDouble())
                .ToArray();

            return vector;
        }

        public async Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(IEnumerable<string> inputs, string? model = null)
        {
            var client = CreateClient(model);

            var payload = BinaryData.FromObjectAsJson(new
            {
                model = model ?? _defaultModel,
                input = inputs.ToArray(),
                encoding_format = "float"
            });

            using var content = BinaryContent.Create(payload);
            ClientResult result = await client.GenerateEmbeddingsAsync(content);
            BinaryData output = result.GetRawResponse().Content;

            using var doc = JsonDocument.Parse(output.ToString());
            var data = doc.RootElement.GetProperty("data");

            return data.EnumerateArray()
                .Select(item => item.GetProperty("embedding")
                    .EnumerateArray()
                    .Select(x => (float)x.GetDouble())
                    .ToArray())
                .ToList();
        }
    }
}
