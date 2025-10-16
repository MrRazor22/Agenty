using Agenty.AgentCore;
using Agenty.AgentCore.Flows;
using Agenty.LLMCore.BuiltInTools;

namespace TestApp
{
    public static class ConsoleRunner
    {
        public static async Task RunAsync()
        {
            Agent? app = null;
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

                app.WithSystemPrompt("You are an execution-oriented assistant. Answer in a user friendly way. You can't run code, dont generate or suggest code. Follow user goals precisely, plan minimal steps, execute tools efficiently, and never repeat identical tool calls.")
                    .WithTools<GeoTools>()
                    .WithTools<WeatherTool>()
                    .WithTools<ConversionTools>()
                    .WithTools<MathTools>()
                    .WithTools<SearchTools>()
                    .Use(() => new ReflectionStep("publisherme/llama/llama-3.2-3b-instruct-q4_k_m.gguf"))
                    .Use(() => new FinalSummaryStep())
                    .Use(() => new ToolCallingStep())
                    .Use(() => new PlanningStep("phi"));

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

                    // Simple
                    Console.WriteLine("\n=== Agent Result ===");
                    Console.WriteLine(result.Message);

                    // Diagnostics - flat access
                    Console.WriteLine("Duration: " + result.Diagnostics.Duration.TotalSeconds + "s");
                    Console.WriteLine("Total: " + result.Diagnostics.TotalTokens.Total + " tokens");
                    Console.WriteLine("Input: " + result.Diagnostics.TotalTokens.InputTokens);
                    Console.WriteLine("Output: " + result.Diagnostics.TotalTokens.OutputTokens);

                    // Breakdown
                    foreach (var kvp in result.Diagnostics.TokensBySource)
                    {
                        var step = kvp.Key;
                        var usage = kvp.Value;
                        Console.WriteLine("  " + step + ": " + usage.Total + " tokens");
                    }
                }
            }
            finally
            {
                await app?.SaveHistoryAsync("default");
            }
        }
    }
}
