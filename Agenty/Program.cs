using Agenty.AgentCore;
using Agenty.LLMCore;
using Agenty.LLMCore.BuiltInTools;
using Agenty.LLMCore.Providers.OpenAI;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
using System;
using System.ComponentModel;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ILogger = Agenty.LLMCore.ILogger;

namespace Agenty
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var agent = Agent.Create()
                        .WithLLM("http://127.0.0.1:1234/v1", "lmstudio")
                        .WithTools<MathTools>()
                        .WithTools<WeatherTool>()
                        .WithTools<GeoTools>()
                        .WithTools<ConversionTools>()
                        .WithTools<SearchTools>()
                        .WithComponents(critiqueInterval: 3);

            Console.WriteLine("🤖 Agenty Agent ready. Type 'exit' to quit.");

            while (true)
            {
                Console.Write("\nYou: ");
                string? input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(input) || input.Equals("exit", StringComparison.OrdinalIgnoreCase))
                    break;

                Console.WriteLine("Agent thinking...");

                try
                {
                    string result = await agent.ExecuteAsync(input);
                    Console.WriteLine($"Agent: {result}");
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

