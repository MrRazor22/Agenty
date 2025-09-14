using Agenty.AgentCore;
using Agenty.AgentCore.Executors;
using Agenty.AgentCore.TokenHandling;
using Agenty.LLMCore.BuiltInTools;
using Agenty.LLMCore.Logging;
using Agenty.LLMCore.Providers.OpenAI;
using Agenty.RAG;
using Microsoft.Extensions.Logging;
using IDefaultLogger = Agenty.LLMCore.Logging.IDefaultLogger;

namespace Agenty.Test
{
    public static class RAGToolCallingRunner
    {
        public static async Task RunAsync()
        {
            var solutionRoot = Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..")
            );
            var docsPath = Path.Combine(solutionRoot, "Agenty", "Test", "ExampleDocumentation");

            IDefaultLogger logger = new ConsoleLogger(LogLevel.Trace);

            // Set up embeddings + vector store
            var embeddings = new OpenAIEmbeddingClient("http://127.0.0.1:1234/v1", "lmstudio", "bge-model");
            var store = new InMemoryVectorStore(logger: logger);
            var tokenizer = new SharpTokenTokenizer("gpt-3.5-turbo");

            // Build agent with RAG tools + executor
            var agent = Agent.Create()
                .WithLLM("http://127.0.0.1:1234/v1", "lmstudio", "qwen@q5_k_m")
                .WithLogger(logger)
                .WithRAG(embeddings, store, tokenizer);

            // Load docs into KB through context
            var docs = await RAG.IO.DocumentLoader.LoadDirectoryAsync(docsPath);
            await agent.Context.RAG!.AddDocumentsAsync(docs);

            agent.WithTools(new RAGTools(agent.Context.RAG!))
                .WithExecutor<RAGToolCallingExecutor>();

            Console.WriteLine("🤖 RAG Tool-Calling Agent ready. Type 'exit' to quit.");
            Console.WriteLine("💡 The LLM can decide to call KB search, web search, or ad-hoc text search.");
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
