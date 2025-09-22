using Agenty.AgentCore.Runtime;
using Agenty.LLMCore;
using Agenty.LLMCore.ChatHandling;
using Agenty.RAG;
using Agenty.RAG.IO;
using Agenty.RAG.Stores;

namespace Agenty.AgentCore.Steps.RAG
{
    // 1. Retrieve from KB
    public sealed class KbSearchStep : IAgentStep<object, IReadOnlyList<SearchResult>>
    {
        private readonly IRagRetriever? _overrideRetriever;

        public KbSearchStep(IRagRetriever? retriever = null)
        {
            _overrideRetriever = retriever;
        }

        public async Task<IReadOnlyList<SearchResult>?> RunAsync(IAgentContext ctx, object? input = null)
        {
            var retriever = _overrideRetriever ?? ctx.Memory.LongTerm
                ?? throw new InvalidOperationException("No retriever available for KB search.");

            var query = ctx.Memory.Working.GetCurrentUserRequest();
            if (string.IsNullOrWhiteSpace(query))
                throw new ArgumentException("No user query found for KB search.");

            return await retriever.Search(query, topK: 3);
        }
    }

    // 2. Fallback to web if KB is weak
    public sealed class WebFallbackStep : IAgentStep<IReadOnlyList<SearchResult>, IReadOnlyList<SearchResult>>
    {
        private readonly IRagRetriever? _overrideRetriever;

        public WebFallbackStep(IRagRetriever? retriever = null)
        {
            _overrideRetriever = retriever;
        }

        public async Task<IReadOnlyList<SearchResult>?> RunAsync(IAgentContext ctx, IReadOnlyList<SearchResult>? kbResults = null)
        {
            var retriever = _overrideRetriever ?? ctx.Memory.LongTerm
                ?? throw new InvalidOperationException("No retriever available for web fallback.");

            var goal = ctx.Memory.Working.GetCurrentUserRequest()
                ?? throw new InvalidOperationException("No user query found for web fallback.");

            var webDocs = await WebSearchLoader.SearchAsync(goal, maxResults: 3);
            if (webDocs.Any())
            {
                await retriever.AddDocumentsAsync(
                    webDocs.Select(d => new Document(d.Doc, d.Source)));

                var webResults = await retriever.Search(goal, topK: 3);

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
        public Task<string?> RunAsync(IAgentContext ctx, IReadOnlyList<SearchResult>? results = null)
        {
            var contextText = results?.Any() == true
                ? string.Join("\n\n", results.Select(r => $"[{r.Source}] {r.Content}"))
                : string.Empty;

            ctx.Memory.Working.Add(Role.System, "You are a friendly assistant. Use retrieved context if provided.");
            if (!string.IsNullOrEmpty(contextText))
                ctx.Memory.Working.Add(Role.System, "Context:\n" + contextText);

            return Task.FromResult<string?>(contextText);
        }
    }
}
