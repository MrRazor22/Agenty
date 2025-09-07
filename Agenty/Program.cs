// File: Program.cs
using Agenty.AgentCore;
using Agenty.LLMCore.Logging;
using Agenty.LLMCore.Providers.OpenAI;
using Agenty.RAG;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;
using ILogger = Agenty.LLMCore.Logging.ILogger;

namespace Agenty
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            ILogger logger = new ConsoleLogger(LogLevel.Trace);

            // Create RagCoordinator first (handles ingestion + search)
            var coord = new RagCoordinator(
                new OpenAIEmbeddingClient(
                    "http://127.0.0.1:1234/v1",   // LM Studio URL
                    "lmstudio",                   // dummy API key
                    "publisherme/bge/bge-base-en-v1.5-q4_k_m.gguf" // embedding model
                ),
                logger
            );

            // Inject into RAGAgent (handles reasoning + QA loop)
            var agent = RAGAgent.Create(coord)
                .WithLLM("http://127.0.0.1:1234/v1", "lmstudio", "qwen/qwen3-4b-2507")
                .WithLogger(logger);

            const string kbPath = "kb.json";

            // Load existing KB if present
            agent.LoadKnowledge(kbPath);

            // Add knowledge base (docs, directory, or URLs)
            await coord.AddDirectoryAsync("D:\\CodeBase\\Agenty\\Agenty\\Test\\ExampleDocumentation");

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

            // Save KB on exit
            agent.SaveKnowledge(kbPath);
            Console.WriteLine("💾 Knowledge base saved.");
            Console.WriteLine("👋 Exiting Agenty.");
        }
    }
}
