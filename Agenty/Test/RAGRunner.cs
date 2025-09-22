using Agenty.AgentCore;
using Agenty.AgentCore.Steps;
using Agenty.AgentCore.Steps.RAG;
using Agenty.AgentCore.TokenHandling;
using Agenty.LLMCore;
using Agenty.LLMCore.Logging;
using Agenty.RAG;
using Agenty.RAG.Embeddings.Providers.OpenAI;
using Agenty.RAG.IO;
using Agenty.RAG.Stores;
using Microsoft.Extensions.Logging;
using OpenAI.Responses;

namespace Agenty.Test
{
    public static class RAGRunner
    {
        public static async Task RunAsync()
        {
            var solutionRoot = Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..")
            );
            var docsPath = Path.Combine(solutionRoot, "Agenty", "Test", "ExampleDocumentation");
            ILogger logger = new ConsoleLogger("RAGRunner", LogLevel.Debug);

            // Setup retriever: embeddings + in-memory vector store
            var retriever = new RagRetriever(
                embeddings: new OpenAIEmbeddingClient("http://127.0.0.1:1234/v1", "lmstudio", "publisherme/bge/bge-large-en-v1.5-q4_k_m.gguf"),
                store: new FileVectorStore(),
                tokenizer: new SharpTokenTokenizer("gpt-3.5-turbo"),
                logger: logger
            );

            var agent = Agent.Create()
                .WithLLM("http://127.0.0.1:1234/v1", "lmstudio", "qwen@q5_k_m")
                .WithLogger(logger)
                .WithRAGRetriever(retriever)
                .WithExecutor(
                    new StepExecutor.Builder()
                        .Add(new KbSearchStep(retriever))   // search KB
                        .Branch<IReadOnlyList<SearchResult>>(
                            results => results!.Any() || results!.Max(r => r.Score) < 0.6,
                            onWeak => onWeak.Add(new WebFallbackStep(retriever))
                        )
                        .Add(new ContextBuildStep())        // add context into chat
                        .Add(new LoopStep(
                            new StepExecutor.Builder()
                                .Add(new ResponseStep())         // model answers
                                .Add(new SummarizationStep())    // summarize
                                .Add(new EvaluationStep())       // check quality
                                .Branch<Answer, string>(
                                    ans => ans?.confidence_score is Verdict.yes or Verdict.partial,
                                    onYes => onYes.Add(new FinalizeStep()),
                                    onNo => onNo.Add(new ReplanningStep())
                                )
                                .Build()
                        ))
                        .Build()
                );

            // Load knowledge base docs
            var docs = await DocumentLoader.LoadDirectoryAsync(docsPath);
            await retriever.AddDocumentsAsync(docs, batchSize: 128, maxParallel: 4);

            Console.WriteLine("🤖 RAG Agent ready. Type 'exit' to quit.");
            Console.WriteLine("💡 Searches KB first, then falls back to web if needed.");
            Console.WriteLine();

            while (true)
            {
                Console.Write("You: ");
                string? input = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(input) ||
                    input.Equals("exit", StringComparison.OrdinalIgnoreCase))
                    break;

                Console.WriteLine("\n" + new string('=', 60));

                try
                {
                    var answer = await agent.ExecuteAsync(input);

                    Console.WriteLine("\n🤖 Agent Response:");
                    Console.WriteLine(new string('-', 40));
                    Console.WriteLine(answer);
                    Console.WriteLine(new string('=', 60));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"\n❌ Error: {ex.Message}");
                    Console.WriteLine(new string('=', 60));
                }
            }

            Console.WriteLine("\n👋 Exiting Agenty.");
        }
    }
}
