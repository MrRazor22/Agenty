using Agenty.AgentCore;
using Agenty.AgentCore.Executors;
using Agenty.AgentCore.Steps;
using Agenty.AgentCore.TokenHandling;
using Agenty.BuiltInTools;
using Agenty.LLMCore.Logging;
using Agenty.LLMCore.Providers.OpenAI;
using Agenty.RAG;
using Agenty.RAG.Embeddings.Providers.OpenAI;
using Agenty.RAG.IO;
using Agenty.RAG.Stores;
using Microsoft.Extensions.Logging;

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

            ILogger logger = new ConsoleLogger("RAGToolCallingRunner", LogLevel.Trace);

            // Set up embeddings + vector store
            var embeddings = new OpenAIEmbeddingClient(
                "http://127.0.0.1:1234/v1",
                "lmstudio",
                "bge-model"
            );

            var store = new InMemoryVectorStore();
            var tokenizer = new SharpTokenTokenizer("gpt-3.5-turbo");

            // Build agent with RAG retriever, tools, and tool-calling loop executor
            var agent = Agent.Create()
                .WithSystemPrompt("You are a helpful assistant with access to retrieval tools. " +
                            "Always prefer using the knowledge base when relevant. " +
                            "If no knowledge base results are useful, use web search. " +
                            "For custom input text, use the ad-hoc search tool. " +
                            "Keep answers short, factual, and always cite sources if available.")
                .WithLLM("http://127.0.0.1:1234/v1", "lmstudio", "qwen@q5_k_m")
                .WithLogger(logger)
                .WithRAG(embeddings, store)
                .WithExecutor(
                    new StepExecutor.Builder()
                        // Initial setup / system prompt
                        .Add(new PlanningStep())
                        // Main tool-calling + refinement loop
                        .Add(new LoopStep(
                            new StepExecutor.Builder()
                                .Add(new ToolCallingStep())       // let LLM invoke RAGTools
                                .Add(new SummarizationStep())     // condense tool outputs
                                .Add(new EvaluationStep())        // check confidence
                                .Branch<Answer, string>(
                                    ans => ans?.confidence_score is Verdict.yes or Verdict.partial,
                                    onYes => onYes.Add(new FinalizeStep()),   // finalize answer
                                    onNo => onNo.Add(new ReplanningStep())   // retry/replan
                                )
                                .Build()
                        ))
                        .Build()
                );

            var kb = agent.Context.Memory.LongTerm;
            agent.WithTools(new RAGTools(kb));

            // Load docs into KB
            var docs = await DocumentLoader.LoadDirectoryAsync(docsPath);
            await kb!.AddDocumentsAsync(docs);

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
