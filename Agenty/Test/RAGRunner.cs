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

            // Load knowledge base documents
            var docs = await RAG.IO.DocumentLoader.LoadDirectoryAsync(docsPath);
            await agent.Knowledge.AddDocumentsAsync(docs);

            Console.WriteLine("🤖 RAG Agent ready. Type 'exit' to quit.");
            Console.WriteLine("💡 The agent will search knowledge base first, then fallback to web if needed.");
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