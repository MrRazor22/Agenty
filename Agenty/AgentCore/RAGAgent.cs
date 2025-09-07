using Agenty.LLMCore;
using Agenty.LLMCore.Logging;
using Agenty.LLMCore.Providers.OpenAI;
using Agenty.LLMCore.ToolHandling;
using SharpToken;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Agenty.AgentCore
{
    public sealed class RAGAgent
    {
        private ILLMClient _llm = null!;
        private IEmbeddingClient _embeddings = null!;
        private ToolCoordinator _coord = null!;
        private readonly IToolRegistry _tools = new ToolRegistry();
        private readonly List<KBEntry> _knowledgeBase = new();

        private readonly Conversation chat = new();
        private ILogger _logger = null!;
        private Gate? _grader;

        // Config
        private double _similarityThreshold = 0.3;
        private int _maxTokensContext = 1500;
        private int _chunkSize = 200;
        private int _chunkOverlap = 50;
        private string _tokenizerModel = "gpt-3.5-turbo";

        public static RAGAgent Create() => new();
        private RAGAgent() { }

        // === Public config ===
        public RAGAgent WithLLM(string baseUrl, string apiKey, string model = "any_model")
        {
            _llm = new OpenAILLMClient();
            _llm.Initialize(baseUrl, apiKey, model);
            _coord = new ToolCoordinator(_llm, _tools);
            return this;
        }

        public RAGAgent WithEmbeddings(IEmbeddingClient embeddingClient) { _embeddings = embeddingClient; return this; }
        public RAGAgent WithLogger(ILogger logger) { _logger = logger; _grader = new Gate(_coord, _logger); _logger.AttachTo(chat); return this; }
        public RAGAgent WithThreshold(double threshold) { _similarityThreshold = threshold; return this; }
        public RAGAgent WithContextBudget(int tokens) { _maxTokensContext = tokens; return this; }
        public RAGAgent WithChunking(int chunkSize, int overlap) { _chunkSize = chunkSize; _chunkOverlap = overlap; return this; }
        public RAGAgent WithTokenizerModel(string model) { _tokenizerModel = model; return this; }

        // === Knowledge base entry ===
        public record KBEntry(string Id, string Source, string Text, float[] Vector, DateTime Added);

        // === Add docs ===
        public async Task<RAGAgent> AddDocumentsAsync(IEnumerable<string> docs, string source = "unknown", CancellationToken ct = default)
        {
            EnsureEmbeddings();

            // split docs semantically first
            var sections = docs.SelectMany(RAGHelper.SplitByParagraphs).ToList();

            // then tokenize into overlapping chunks
            var chunks = sections.SelectMany(s => RAGHelper.Chunk(s, _chunkSize, _chunkOverlap, _tokenizerModel))
                                 .Distinct()
                                 .ToList();

            if (chunks.Count == 0) return this;

            const int batchSize = 16;
            for (int i = 0; i < chunks.Count; i += batchSize)
            {
                var batch = chunks.Skip(i).Take(batchSize).ToList();
                float[][] vectors = await Retry(async () =>
                    (await _embeddings.GetEmbeddingsAsync(batch)).ToArray(), ct);

                for (int j = 0; j < batch.Count; j++)
                {
                    var vec = Normalize(vectors[j]);
                    _knowledgeBase.Add(new KBEntry(Guid.NewGuid().ToString(), source, batch[j], vec, DateTime.UtcNow));
                }
            }
            return this;
        }

        public Task<RAGAgent> AddDocumentAsync(string doc, string source = "unknown", CancellationToken ct = default) =>
            AddDocumentsAsync(new[] { doc }, source, ct);

        // === Search ===
        public async Task<IEnumerable<(string chunk, double score)>> SearchAsync(string query, int topK = 3, CancellationToken ct = default)
        {
            EnsureEmbeddings();
            if (!_knowledgeBase.Any())
                return Enumerable.Empty<(string, double)>();

            var qVec = Normalize(await _embeddings.GetEmbeddingAsync(query));

            return _knowledgeBase
                .Select(k => (k.Text, score: RAGHelper.CosineSimilarity(qVec, k.Vector)))
                .Where(x => x.score >= _similarityThreshold)
                .OrderByDescending(x => x.score)
                .Take(topK)
                .ToList();
        }

        // === RAG execution ===
        public async Task<string> ExecuteAsync(string goal, int maxRounds = 5, int topK = 3, CancellationToken ct = default)
        {
            var retrieved = await SearchAsync(goal, topK, ct);
            var contextChunks = retrieved.Select(r => r.chunk).ToList();

            string context = RAGHelper.AssembleContext(contextChunks, _maxTokensContext, _tokenizerModel);

            chat.Add(Role.System, "You are a concise QA assistant. Use retrieved context if provided. Answer in <=3 sentences.")
                .Add(Role.User, context + "\n\nQuestion: " + goal);

            for (int round = 0; round < maxRounds; round++)
            {
                var response = await _llm.GetResponse(chat);
                chat.Add(Role.Assistant, response);

                var grade = await _grader!.CheckAnswer(goal, chat.ToString(~ChatFilter.System));
                if (grade.confidence_score == Verdict.yes)
                    return await _llm.GetResponse(
                        chat.Add(Role.User, response),
                        LLMMode.Creative);

                chat.Add(Role.User, grade.explanation);
            }

            chat.Add(Role.User, "Max rounds reached. Return the best final answer now as plain text.");
            return await _llm.GetResponse(chat);
        }

        // === Persistence ===
        public void SaveKnowledgeBase(string path)
        {
            var json = JsonSerializer.Serialize(_knowledgeBase);
            File.WriteAllText(path, json);
        }

        public void LoadKnowledgeBase(string path)
        {
            if (!File.Exists(path)) return;
            var json = File.ReadAllText(path);
            var list = JsonSerializer.Deserialize<List<KBEntry>>(json);
            if (list != null) _knowledgeBase.AddRange(list);
        }

        // === Helpers ===
        private void EnsureEmbeddings()
        {
            if (_embeddings == null)
                throw new InvalidOperationException("Embeddings client not configured. Call WithEmbeddings() first.");
        }

        private static async Task<T> Retry<T>(Func<Task<T>> action, CancellationToken ct, int retries = 3, int delayMs = 500)
        {
            for (int i = 0; i < retries; i++)
            {
                try { return await action(); }
                catch when (i < retries - 1) { await Task.Delay(delayMs * (i + 1), ct); }
            }
            throw new Exception("Retry failed after multiple attempts.");
        }

        private static float[] Normalize(float[] vector)
        {
            double norm = Math.Sqrt(vector.Sum(v => v * v));
            return norm == 0 ? vector : vector.Select(v => (float)(v / norm)).ToArray();
        }
    }

    // === RAG Helpers ===
    public static class RAGHelper
    {
        public static IEnumerable<string> SplitByParagraphs(string text)
        {
            return text.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries)
                       .Select(p => p.Trim())
                       .Where(p => !string.IsNullOrWhiteSpace(p));
        }

        public static double CosineSimilarity(IReadOnlyList<float> v1, IReadOnlyList<float> v2)
        {
            if (v1.Count != v2.Count) throw new ArgumentException("Vectors must have same length");

            double dot = 0, norm1 = 0, norm2 = 0;
            for (int i = 0; i < v1.Count; i++)
            {
                dot += v1[i] * v2[i];
                norm1 += v1[i] * v1[i];
                norm2 += v2[i] * v2[i];
            }

            return (norm1 == 0 || norm2 == 0) ? 0 : dot / (Math.Sqrt(norm1) * Math.Sqrt(norm2));
        }

        public static IEnumerable<string> Chunk(string text, int maxTokens, int overlap, string model)
        {
            var encoder = GptEncoding.GetEncodingForModel(model);
            var tokens = encoder.Encode(text);

            int start = 0;
            while (start < tokens.Count)
            {
                var end = Math.Min(start + maxTokens, tokens.Count);
                var chunkTokens = tokens.GetRange(start, end - start);
                yield return encoder.Decode(chunkTokens);
                start += maxTokens - overlap;
            }
        }

        public static string AssembleContext(IEnumerable<string> chunks, int maxTokens, string model)
        {
            var encoder = GptEncoding.GetEncodingForModel(model);

            var context = new List<string>();
            int tokens = 0;

            foreach (var chunk in chunks)
            {
                int chunkTokens = encoder.Encode(chunk).Count;
                if (tokens + chunkTokens > maxTokens) break;
                context.Add(chunk);
                tokens += chunkTokens;
            }

            return context.Any() ? "Use the following context:\n\n" + string.Join("\n\n", context) : "";
        }
    }
}
