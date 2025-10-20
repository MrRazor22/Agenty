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
                builder.WithLogLevel(Microsoft.Extensions.Logging.LogLevel.Debug);
                // === 4. Build app (ASP.NET: var app = builder.Build()) ===
                app = builder.Build();

                await app.LoadHistoryAsync("default");

                app.WithSystemPrompt("You are a helpful assistant.")
                    .WithTools<GeoTools>()
                    .WithTools<WeatherTool>()
                    .WithTools<ConversionTools>()
                    .WithTools<MathTools>()
                    .WithTools<SearchTools>()
                    .Use<ErrorHandlingStep>()
                    .Use(() => new FinalSummaryStep())
                    .Use(() => new ToolCallingStep())
                    .Use(() => new PlanningStep());

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
                }
            }
            finally
            {
                await app?.SaveHistoryAsync("default");
            }
        }
    }
}
