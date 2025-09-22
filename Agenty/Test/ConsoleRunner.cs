using Agenty.AgentCore;
using Agenty.AgentCore.Executors;
using Agenty.LLMCore.Logging;
using Microsoft.Extensions.Logging;

namespace Agenty.Test;

public static class ConsoleRunner
{
    public static async Task RunAsync()
    {
        ILogger logger = new ConsoleLogger("Agent", LogLevel.Trace);

        var agent = Agent.Create()
            .WithSystemPrompt("You are a helpful assistant.")
            .WithLLM("http://127.0.0.1:1234/v1", "lmstudio", "qwen@q5_k_m")
            .WithLogger(logger)
            .WithExecutor(new PlanningExecutor(maxRounds: 50)); // or RagToolCallingExecutor, etc.

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
