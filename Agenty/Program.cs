using Agenty.AgentCore;
using Agenty.LLMCore;
using OpenAI.Chat;
using System;
using System.ComponentModel;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Agenty
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var llm = new LLMCore.OpenAIClient();
            llm.Initialize("http://127.0.0.1:1234/v1", "lmstudio", "any_model");

            // Register tools
            ITools tools = new Tools();
            tools.Register(UserTools.WikiSummary, UserTools.CurrencyConverter);

            var chat = new ChatHistory();
            Console.WriteLine("🤖 Welcome to Agenty ChatBot! Type 'exit' to quit.\n");

            while (true)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("You: ");
                Console.ResetColor();

                string? input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(input) || input.Equals("exit", StringComparison.OrdinalIgnoreCase))
                    break;

                chat.Add(Role.User, input);

                Tool toolCall;
                try
                {
                    toolCall = await llm.GetToolCallResponse(chat, tools);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Error fetching tool response: {ex.Message}");
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(toolCall.AssistantMessage))
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"🤖 LLM: {toolCall.AssistantMessage}");
                    Console.ResetColor();
                    chat.Add(Role.Assistant, toolCall.AssistantMessage);
                    continue;
                }

                // Call the tool
                try
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"🔧 Tool Call → {toolCall}");
                    Console.ResetColor();

                    object? result = tools.Invoke<object>(toolCall);

                    Console.ForegroundColor = ConsoleColor.White;
                    Console.WriteLine($"📄 Result: {result}");
                    Console.ResetColor();

                    chat.Add(Role.Tool, $"Result from {toolCall.Name}: {result}", toolCall);
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("⚠️ Tool invocation failed: " + ex.Message);
                    Console.ResetColor();
                }
            }

            Console.WriteLine("👋 Exiting Agenty ChatBot.");
        }

        static class UserTools
        {
            [Description("Gets a summary of a Wikipedia topic.")]
            public static string WikiSummary([Description("Title of the Wikipedia article")] string topic)
            {
                using var client = new HttpClient();

                try
                {
                    var searchUrl = $"https://en.wikipedia.org/w/api.php?action=query&list=search&srsearch={Uri.EscapeDataString(topic)}&format=json";
                    var searchJson = client.GetStringAsync(searchUrl).Result;
                    var searchObj = JsonNode.Parse(searchJson);
                    var title = searchObj?["query"]?["search"]?[0]?["title"]?.ToString();

                    if (string.IsNullOrWhiteSpace(title))
                        return "No matching Wikipedia article found.";

                    var summaryUrl = $"https://en.wikipedia.org/api/rest_v1/page/summary/{Uri.EscapeDataString(title)}";
                    var summaryJson = client.GetStringAsync(summaryUrl).Result;
                    var summaryObj = JsonNode.Parse(summaryJson);
                    return summaryObj?["extract"]?.ToString() ?? "No summary found.";
                }
                catch
                {
                    return "Failed to fetch Wikipedia data.";
                }
            }

            [Description("Converts an amount from one currency to another using exchange rates.")]
            public static string CurrencyConverter(
                [Description("Currency code to convert from (e.g., USD)")] string from,
                [Description("Currency code to convert to (e.g., INR)")] string to,
                [Description("Amount to convert")] decimal amount)
            {
                using var client = new HttpClient();
                try
                {
                    var url = $"https://api.exchangerate.host/convert?from={from}&to={to}&amount={amount}";
                    var json = client.GetStringAsync(url).Result;
                    var obj = JsonNode.Parse(json);
                    var result = obj?["result"]?.ToString();

                    return result != null
                        ? $"{amount} {from} = {result} {to}"
                        : "Conversion failed.";
                }
                catch
                {
                    return "Currency conversion API failed.";
                }
            }
        }
    }
}
