using Agenty.AgentCore;
using Agenty.LLMCore.Logging;
using Agenty.LLMCore.Providers.OpenAI;
using Agenty.RAG;
using Microsoft.Extensions.Logging;
using ILogger = Agenty.LLMCore.Logging.ILogger;

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

            ILogger logger = new ConsoleLogger(LogLevel.Trace);

            var agent = RAGAgent.Create()
                .WithLLM("http://127.0.0.1:1234/v1", "lmstudio", "qwen@q5_k_m")
                .WithLogger(logger)
                .WithRAG(
                    new OpenAIEmbeddingClient("http://127.0.0.1:1234/v1", "lmstudio", "bge-model"),
                    new InMemoryVectorStore(logger: logger),
                    new SharpTokenTokenizer("gpt-3.5-turbo"),
                    logger
                );

            // ✅ Use RAGHelper to load files, then add docs
            var docs = await RAG.IO.DocumentLoader.LoadDirectoryAsync(docsPath);
            await agent.Knowledge.AddDocumentsAsync(docs);

            Console.WriteLine("🤖 RAG Agent ready. Type 'exit' to quit.");
            while (true)
            {
                Console.Write("\nYou: ");
                string? input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(input) ||
                    input.Equals("exit", StringComparison.OrdinalIgnoreCase))
                    break;

                Console.WriteLine("Agent thinking...");

                try
                {
                    var result = await agent.ExecuteAsync(input);

                    Console.WriteLine("==============================================================");
                    Console.WriteLine($"Agent: {result.Answer}");
                    Console.WriteLine("--------------------------------------------------------------");

                    if (result.Sources.Any())
                    {
                        Console.WriteLine("Sources:");
                        foreach (var (src, score) in result.Sources)
                            Console.WriteLine($" - {src} ({score:F3})");
                    }
                    else
                    {
                        Console.WriteLine("Sources: [none]");
                    }
                    Console.WriteLine("==============================================================");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }
            }

            Console.WriteLine("👋 Exiting Agenty.");
        }
    }
}
