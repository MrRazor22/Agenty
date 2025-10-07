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
    public static class ConsoleRunner
    {
        public static async Task RunAsync()
        {
            // === 1. Setup DI ===
            var services = new ServiceCollection();

            // typed logging (ILogger<T>)
            services.AddSingleton(typeof(ILogger<>), typeof(ConsoleLogger<>));

            // register LLM client
            services.AddSingleton<ILLMClient>(sp =>
            {
                var client = new OpenAILLMClient();
                client.Initialize("http://127.0.0.1:1234/v1", "lmstudio", "qwen@q5_k_m");
                return client;
            });

            // register coordinator
            services.AddSingleton<ILLMCoordinator>(sp =>
            {
                var llmClient = sp.GetRequiredService<ILLMClient>();
                var registry = new ToolRegistry();
                var runtime = new ToolRuntime(registry);
                var parser = new ToolCallParser();
                var retry = new DefaultRetryPolicy();
                return new LLMCoordinator(llmClient, registry, runtime, parser, retry);
            });

            // === 2. Build Agent ===
            var agent = Agent.Create(services)
            .WithFlow(
                new AgentPipelineBuilder()
                    .Use<PlanningStep>()
                    .Use(() => new ToolCallingStep(ToolCallMode.Auto, ReasoningMode.Balanced))
                    .Use<ReflectionStep>()
                    .Use<FinalizationStep>()
                    .Build()
            )
            .WithTools<GeoTools>()
            .WithTools<WeatherTool>()
            .WithTools<ConversionTools>()
            .WithTools<MathTools>()
            .WithSystemPrompt("You are a helpful assistant that answers concisely.");

            while (true)
            {
                // === 3. Run Goal ===
                Console.WriteLine("Enter your goal: ");
                var goal = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(goal))
                {
                    Console.WriteLine("No goal entered. Exiting.");
                    return;
                }

                var result = await agent.ExecuteAsync(goal);

                // === 4. Print result ===
                Console.WriteLine("=== Agent Result ===");
                Console.WriteLine(result.Message ?? "(no answer)");
            }
        }
    }
}
