using Agenty.AgentCore;
using Agenty.LLMCore.BuiltInTools;
using Agenty.LLMCore.Logging;
using Microsoft.Extensions.Logging;
using ILogger = Agenty.LLMCore.Logging.ILogger;

namespace Agenty.Test
{
    public static class ToolCallingRunner
    {
        public static async Task RunAsync()
        {
            ILogger logger = new ConsoleLogger(LogLevel.Trace);

            var agent = Agent.Create()
                .WithLLM("http://127.0.0.1:1234/v1", "lmstudio", "qwen@q5_k_m")
                .WithLogger(logger)
                .WithTools<SearchTools>()
                .WithTools<GeoTools>()
                .WithTools<WeatherTool>()
                .WithTools<ConversionTools>()
                .WithTools<MathTools>()
                .WithExecutor<ToolCallingExecutor>(); // plug in strategy

            Console.WriteLine("🤖 Agenty ToolCalling Agent ready. Type 'exit' to quit.");

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
                    string result = await agent.ExecuteAsync(input);
                    Console.WriteLine("==============================================================");
                    Console.WriteLine($"Agent: {result}");
                    Console.WriteLine("==============================================================");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Error: {ex.Message}");
                }
            }

            Console.WriteLine("👋 Exiting Agenty.");
        }
    }
}
