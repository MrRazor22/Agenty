using Agenty.AgentCore;
using Agenty.AgentCore.Executors;
using Agenty.AgentCore.Steps;
using Agenty.LLMCore.BuiltInTools;
using Agenty.LLMCore.Logging;
using Microsoft.Extensions.Logging;

namespace Agenty.Test
{
    public static class ToolCallingRunner
    {
        public static async Task RunAsync()
        {
            ILogger logger = new ConsoleLogger("ToolCallingRunner", LogLevel.Trace);

            var agent = Agent.Create()
                .WithLLM("http://127.0.0.1:1234/v1", "lmstudio", "qwen@q5_k_m")
                .WithLogger(logger)
                .WithTools<GeoTools>()
                .WithTools<WeatherTool>()
                .WithTools<ConversionTools>()
                .WithTools<MathTools>()
                //.WithExecutor(new ToolCallingExecutor(100)); // pick your maxRounds
                .WithExecutor(
                    new StepExecutor.Builder()
                        .Add(new PlanningStep("Plan how to solve"))   // run once
                        .Add(new LoopStep(
                            new StepExecutor.Builder()
                                .Add(new ToolCallingStep())
                                .Add(new SummarizationStep("Summarize session"))
                                .Add(new EvaluationStep("Did it solve?"))
                                .Branch<Answer, string>(
                                    ans => ans?.confidence_score is Verdict.yes or Verdict.partial,
                                    onYes => onYes.Add(new FinalizeStep()),
                                    onNo => onNo.Add(new ReplanningStep("Replan strategy"))
                                )
                                .Build()
                        ))
                        .Build()
                );

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
