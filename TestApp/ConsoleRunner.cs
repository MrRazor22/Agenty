using Agenty.AgentCore;
using Agenty.AgentCore.Flows;
using Agenty.AgentCore.Runtime;
using Agenty.LLMCore;
using Agenty.LLMCore.BuiltInTools;
using Agenty.LLMCore.Logging;
using Agenty.LLMCore.Providers.OpenAI;
using Agenty.LLMCore.ToolHandling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TestApp
{
    public static class Program
    {
        public static async Task Main()
        {
            Agent app = null;
            try
            {
                // === 1. Create builder (ASP.NET: WebApplication.CreateBuilder) ===
                var builder = Agent.CreateBuilder();

                builder.AddOpenAI(opts =>
                {
                    opts.BaseUrl = "http://127.0.0.1:1234/v1";
                    opts.ApiKey = "lmstudio";
                    opts.Model = "qwen@q5_k_m";
                });

                // === 4. Build app (ASP.NET: var app = builder.Build()) ===
                app = builder.Build();

                await app.LoadHistoryAsync("default");

                app.WithSystemPrompt("You are a helpful assistant that answers concisely.")
                    .WithTools<GeoTools>()
                    .WithTools<WeatherTool>()
                    .WithTools<ConversionTools>()
                    .WithTools<MathTools>()
                    .Use<PlanningStep>()
                    .Use(() => new ToolCallingStep(ToolCallMode.Auto, ReasoningMode.Balanced))
                    .Use<ReflectionStep>()
                    .Use<FinalSummaryStep>();

                // === 5. Run (ASP.NET: app.Run()) ===
                while (true)
                {
                    Console.WriteLine("Enter your goal: ");
                    var goal = Console.ReadLine();

                    if (string.IsNullOrWhiteSpace(goal))
                    {
                        Console.WriteLine("No goal entered. Exiting.");
                        return;
                    }

                    var result = await app.ExecuteAsync(goal);
                    Console.WriteLine("\n=== Agent Result ===");
                    Console.WriteLine("Response: " + result.Message);
                    Console.WriteLine("Time: " + result.Duration);
                    Console.WriteLine("Tokens: " + result.TokensUsed);
                }
            }
            finally
            {
                await app.SaveHistoryAsync("default");
            }
        }
    }
}
