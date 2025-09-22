using Agenty.LLMCore;
using Agenty.LLMCore.ChatHandling;
using Agenty.RAG;
using Agenty.RAG.IO;
using Agenty.RAG.Stores;

namespace Agenty.AgentCore.Steps.RAG
{
    // 1. Retrieve from KB
    public sealed class KbSearchStep : IAgentStep<string, IReadOnlyList<SearchResult>>
    {
        private readonly IRagRetriever _retriever;
        public KbSearchStep(IRagRetriever retriever) => _retriever = retriever;

        public async Task<IReadOnlyList<SearchResult>?> RunAsync(
            Conversation chat, ILLMOrchestrator llm, string? query = null)
        {
            query ??= chat.GetLastUserMessage();
            if (string.IsNullOrWhiteSpace(query))
                throw new ArgumentException("No user query found for KB search.");

            return await _retriever.Search(query, topK: 3);
        }
    }


    // 2. Fallback to web if KB is weak
    public sealed class WebFallbackStep : IAgentStep<IReadOnlyList<SearchResult>, IReadOnlyList<SearchResult>>
    {
        private readonly IRagRetriever _retriever;
        public WebFallbackStep(IRagRetriever retriever) => _retriever = retriever;

        public async Task<IReadOnlyList<SearchResult>?> RunAsync(
            Conversation chat, ILLMOrchestrator llm, IReadOnlyList<SearchResult>? kbResults = null)
        {
            if (chat.GetLastUserMessage() is not string goal)
                throw new InvalidOperationException("No user query found for web fallback.");

            var webDocs = await WebSearchLoader.SearchAsync(goal, maxResults: 3);
            if (webDocs.Any())
            {
                await _retriever.AddDocumentsAsync(
                    webDocs.Select(d => new Document(d.Doc, d.Source)));
                var webResults = await _retriever.Search(goal, topK: 3);

                return (kbResults ?? Array.Empty<SearchResult>())
                    .Concat(webResults)
                    .OrderByDescending(r => r.Score)
                    .Take(3)
                    .ToList();
            }

            return kbResults ?? Array.Empty<SearchResult>();
        }
    }


    // 3. Context builder
    public sealed class ContextBuildStep : IAgentStep<IReadOnlyList<SearchResult>, string>
    {
        public Task<string?> RunAsync(
            Conversation chat, ILLMOrchestrator llm, IReadOnlyList<SearchResult>? results = null)
        {
            var contextText = results?.Any() == true
                ? string.Join("\n\n", results.Select(r => $"[{r.Source}] {r.Content}"))
                : "";

            chat.Add(Role.System, "You are a friendly assistant. Use retrieved context if provided.");
            if (!string.IsNullOrEmpty(contextText))
                chat.Add(Role.System, "Context:\n" + contextText);

            return Task.FromResult<string?>(contextText);
        }
    }

}
