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
            var llm = new OpenAIClient();
            llm.Initialize("http://127.0.0.1:1234/v1", "lmstudio", "any_model");

            ITools tools = new Tools();
            tools.RegisterAll(typeof(UserTools)); // auto-registers static methods in UserTools

            var chat = new ChatHistory();
            chat.Add(Role.System, "You are an assistant. Use tools if needed, or respond directly.");

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
                    ShowError("Error fetching tool call", ex);
                    continue;
                }

                await ExecuteToolChain(toolCall, chat, tools, llm);
            }

            Console.WriteLine("👋 Exiting Agenty ChatBot.");
        }

        private static async Task ExecuteToolChain(Tool initialCall, ChatHistory chat, ITools tools, ILLMClient llm)
        {
            Tool current = initialCall;

            while (true)
            {
                if (!string.IsNullOrWhiteSpace(current.AssistantMessage))
                {
                    ShowMessage("🤖", ConsoleColor.Green, current.AssistantMessage.Trim());
                    chat.Add(Role.Assistant, current.AssistantMessage);
                }

                if (string.IsNullOrWhiteSpace(current.Name))
                    return;

                chat.Add(Role.Assistant, tool: current);
                ShowMessage("🔧 Tool Call", ConsoleColor.Yellow, current.ToString());

                object? result;
                try
                {
                    result = await tools.Invoke<object>(current);
                }
                catch (Exception ex)
                {
                    ShowError("Tool invocation failed", ex);
                    return;
                }

                ShowMessage("📄 Tool Result", ConsoleColor.White, result?.ToString());
                chat.Add(Role.Tool, result?.ToString(), current);

                try
                {
                    current = await llm.GetToolCallResponse(chat, tools);
                }
                catch (Exception ex)
                {
                    ShowError("Error fetching next step", ex);
                    return;
                }

                if (string.IsNullOrWhiteSpace(current.AssistantMessage) && !string.IsNullOrWhiteSpace(current.Name))
                    ShowMessage("🤖", ConsoleColor.Green, $"Calling tool `{current.Name}`...");

                else if (string.IsNullOrWhiteSpace(current.AssistantMessage) &&
                    string.IsNullOrWhiteSpace(current.Name))
                    return;
            }
        }

        private static void ShowMessage(string label, ConsoleColor color, string? text)
        {
            Console.ForegroundColor = color;
            Console.WriteLine($"{label}: {text}");
            Console.ResetColor();
        }

        private static void ShowError(string context, Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"❌ {context}: {ex.Message}");
            Console.ResetColor();
        }
        static class UserTools
        {
            [Description("Gets a summary of a Wikipedia topic.")]
            public static async Task<string> WikiSummary([Description("Title of the Wikipedia article")] string topic)
            {
                using var client = new HttpClient();

                try
                {
                    var searchUrl = $"https://en.wikipedia.org/w/api.php?action=query&list=search&srsearch={Uri.EscapeDataString(topic)}&format=json";
                    var searchJson = await client.GetStringAsync(searchUrl);
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
                try
                {
                    from = from.ToUpperInvariant();
                    to = to.ToUpperInvariant();

                    using var client = new HttpClient();
                    var url = $"https://open.er-api.com/v6/latest/{from}";
                    var response = client.GetStringAsync(url).GetAwaiter().GetResult();
                    var json = JsonNode.Parse(response);

                    var success = json?["result"]?.ToString();
                    if (success != "success")
                    {
                        return $"API error: {json?["error-type"] ?? "Unknown error"}";
                    }

                    var rate = json["rates"]?[to]?.GetValue<decimal?>();

                    if (rate == null)
                        return $"Currency '{to}' not found in rates.";

                    var converted = amount * rate.Value;
                    return $"{amount} {from} = {converted:F2} {to}";
                }
                catch (Exception ex)
                {
                    return $"Currency conversion failed: {ex.Message}";
                }
            }
        }
    }
}
