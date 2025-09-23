using Agenty.AgentCore;
using Agenty.AgentCore.Flows;
using Agenty.AgentCore.TokenHandling;
using Agenty.BuiltInTools;
using Agenty.LLMCore.BuiltInTools;
using Agenty.LLMCore.Logging;
using Agenty.RAG.Embeddings.Providers.OpenAI;
using Agenty.RAG.IO;
using Agenty.RAG.Stores;
using Microsoft.Extensions.Logging;

namespace Agenty.Test;

public static class ConsoleRunner
{
    public static async Task RunAsync()
    {
        var solutionRoot = Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..")
            );
        var docsPath = Path.Combine(solutionRoot, "Agenty", "Test", "ExampleDocumentation");
        var embeddings = new OpenAIEmbeddingClient(
                "http://127.0.0.1:1234/v1",
                "lmstudio",
                "publisherme/bge/bge-large-en-v1.5-q4_k_m.gguf"
            );

        var tokenizer = new SharpTokenTokenizer("gpt-3.5-turbo");
        ILogger logger = new ConsoleLogger("Agent", LogLevel.Trace);

        var agent = Agent.Create()
            .WithSystemPrompt("You are a helpful assistant.")
            .WithLLM("http://127.0.0.1:1234/v1", "lmstudio", "qwen@q5_k_m")
            .WithLogger(logger)
            .WithSessionRAG("lmstudio", "http://127.0.0.1:1234/v1", "publisherme/bge/bge-large-en-v1.5-q4_k_m.gguf")
            .WithKnowledgeBaseRAG("lmstudio", "http://127.0.0.1:1234/v1", "publisherme/bge/bge-large-en-v1.5-q4_k_m.gguf")
            .WithTools<GeoTools>()
            .WithTools<WeatherTool>()
            .WithTools<ConversionTools>()
            .WithTools<MathTools>()
            .WithFlow(new ToolCallingFlow(maxRounds: 50));
        //.WithExecutor(new RagToolCallingExecutor(maxRounds: 50));
        //.WithExecutor(new PlanningExecutor(maxRounds: 50)); 


        var kb = agent.Context.Memory.KnowledgeBase;
        agent.WithTools(new RAGTools(kb));

        // Load docs into KB
        var docs = await DocumentLoader.LoadDirectoryAsync(docsPath);
        await kb!.AddDocumentsAsync(docs, batchSize: 16, maxParallel: 4);

        Console.WriteLine("🤖 Agent ready. Type 'exit' to quit.");
        Console.WriteLine();

        while (true)
        {
            Console.Write("You: ");
            var input = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(input) || input.Equals("exit", StringComparison.OrdinalIgnoreCase))
                break;

            Console.WriteLine(new string('=', 60));

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

        Console.WriteLine("\n👋 Exiting.");
    }
}
