using Agenty.AgentCore;
using Agenty.AgentCore.Steps;
using Agenty.LLMCore;
using Agenty.LLMCore.BuiltInTools;
using System.Text;
using System.Threading;

namespace TestApp
{
    public static class ConsoleRunner
    {
        public static async Task RunAsync()
        {
            Agent? app = null;
            try
            {
                var builder = Agent.CreateBuilder();

                builder.AddOpenAI(opts =>
                {
                    opts.BaseUrl = "http://127.0.0.1:1234/v1";
                    opts.ApiKey = "lmstudio";
                    opts.Model = "publisherme/qwen/qwen3-4b-thinking-2507-q4_k_m.gguf";
                });
                builder.AddRetryPolicy(o =>
                {
                    o.MaxRetries = 3;
                    o.Timeout = TimeSpan.FromMinutes(5);
                });
                builder.WithLogLevel(Microsoft.Extensions.Logging.LogLevel.Trace);

                app = builder.Build();
                await app.LoadHistoryAsync("default");

                app.WithSystemPrompt(
                     "You are an AI agent, execute all user requests faithfully."
                 )
                   .WithTools<GeoTools>()
                   .WithTools<WeatherTool>()
                   .WithTools<ConversionTools>()
                   .WithTools<MathTools>()
                   .WithTools<SearchTools>()
                   .Use(() => new ToolCallingStep(toolMode: ToolCallMode.OneTool))
                   .Use<PlanningStep>()
               .Use(async (ctx, next) =>
               {
                   bool started = false;

                   ctx.Stream = s =>
                   {
                       if (string.IsNullOrWhiteSpace(s))
                           return;

                       if (!started)
                       {
                           Console.Write("[stream] ");
                           started = true;
                       }

                       Console.Write(s);
                   };

                   await next(ctx);
               });


                while (true)
                {
                    Console.Write("Enter your goal (Ctrl+Q to quit):\n");

                    // normal input -> backspace works
                    string goal = Console.ReadLine();
                    if (string.IsNullOrWhiteSpace(goal))
                        continue;

                    using var cts = new CancellationTokenSource();

                    // cancel watcher (does NOT touch input editing)
                    _ = Task.Run(() =>
                    {
                        while (!cts.IsCancellationRequested)
                        {
                            var key = Console.ReadKey(intercept: true);
                            if (key.Key == ConsoleKey.Q && key.Modifiers.HasFlag(ConsoleModifiers.Control))
                            {
                                cts.Cancel();
                                Console.WriteLine("\n-> Cancel requested.");
                                break;
                            }
                        }
                    });

                    Console.WriteLine("\nProcessing...\n");

                    try
                    {
                        var result = await app.ExecuteAsync(goal, cts.Token);

                        var msg = result.Message?.Trim();
                        if (string.IsNullOrWhiteSpace(msg))
                        {
                            Console.WriteLine("[no response]");
                            continue;
                        }

                        Console.WriteLine("\n───────── RESULT ─────────\n");
                        Console.WriteLine(msg);
                        Console.WriteLine("\n──────────────────────────\n");
                    }
                    catch (OperationCanceledException)
                    {
                        Console.WriteLine("\n[Cancelled]\n");
                    }

                }
            }
            finally
            {
                if (app != null)
                    await app.SaveHistoryAsync("default");
            }
        }

    }
}
