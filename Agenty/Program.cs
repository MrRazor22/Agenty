using Agenty.AgentCore;
using Agenty.LLMCore;
using Agenty.LLMCore.OpenAI;
using OpenAI.Chat;
using System;
using System.ComponentModel;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Agenty
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var llm = new OpenAILLMClient();
            llm.Initialize("http://127.0.0.1:1234/v1", "lmstudio", "any_model");

            ToolCoordinator toolCordinator = new ToolCoordinator(llm);

            ITools tools = new ToolManager();
            tools.RegisterAll(typeof(UserTools)); // auto-registers static methods in UserTools

            var chat = new ChatHistory();
            chat.Add(Role.System, "You are an assistant." +
                 "Plan and answer one by one" +
                 "if not sure on what to respind, express that to user directly" +
                 "Use relevant tools if needed, or respond directly." +
                 "Provide your answers short and sweet");


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

                ToolCall toolCall;
                try
                {
                    toolCall = await toolCordinator.GetStructuredToolCall(chat, tools);
                }
                catch (Exception ex)
                {
                    ShowError("Error fetching tool call", ex);
                    continue;
                }

                await ExecuteToolChain(toolCall, chat, tools, toolCordinator);
            }

            Console.WriteLine("👋 Exiting Agenty ChatBot.");
        }

        private static async Task ExecuteToolChain(ToolCall initialCall, ChatHistory chat, ITools tools, ToolCoordinator toolCordinator)
        {
            ToolCall currentToolCall = initialCall;

            while (true)
            {
                if (!string.IsNullOrWhiteSpace(currentToolCall.AssistantMessage))
                {
                    ShowMessage("🤖 Answer", ConsoleColor.Green, currentToolCall.AssistantMessage.Trim());
                    chat.Add(Role.Assistant, currentToolCall.AssistantMessage);
                }

                if (string.IsNullOrWhiteSpace(currentToolCall.Name))
                    return;

                chat.Add(Role.Assistant, tool: currentToolCall);
                ShowMessage("🔧 Tool Call", ConsoleColor.Yellow, currentToolCall.ToString());

                object? result;
                try
                {
                    result = await toolCordinator.Invoke<object>(currentToolCall, tools);
                }
                catch (Exception ex)
                {
                    ShowError("Tool invocation failed", ex);
                    return;
                }

                ShowMessage("📄 Tool Result", ConsoleColor.White, result?.ToString());
                chat.Add(Role.Tool, result?.ToString(), currentToolCall);

                try
                {
                    currentToolCall = await toolCordinator.GetStructuredToolCall(chat, tools);
                }
                catch (Exception ex)
                {
                    ShowError("Error fetching next step", ex);
                    return;
                }

                if (string.IsNullOrWhiteSpace(currentToolCall.AssistantMessage) && !string.IsNullOrWhiteSpace(currentToolCall.Name))
                    ShowMessage("🤖", ConsoleColor.Green, $"Calling tool `{currentToolCall.Name}`...");

                else if (string.IsNullOrWhiteSpace(currentToolCall.AssistantMessage) &&
                    string.IsNullOrWhiteSpace(currentToolCall.Name))
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

        [Description("Sunny, Cloudy, Rainy, Snowy.")]
        public enum WeatherType
        {
            [Description("Clear sky with lots of sunshine.")]
            Sunny,

            [Description("Clouds covering most of the sky.")]
            Cloudy,

            [Description("Water droplets falling from the sky.")]
            Rainy,

            [Description("Frozen crystals falling as snow.")]
            Snowy
        }

        static class UserTools
        {
            [Description("Suggests the ideal weather activity to do")]
            public static string SuggestActivity(
    [Description("The current weather condition.")] WeatherType currentWeather)
            {
                return currentWeather switch
                {
                    WeatherType.Sunny => "perfect for outdoor activities",
                    WeatherType.Cloudy => "mild weather, maybe a walk",
                    WeatherType.Rainy => "stay indoors, read or relax",
                    WeatherType.Snowy => "skiing or snow funx",
                    _ => "Die just kidding"
                };
            }

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
                    from = from?.ToUpperInvariant() ?? throw new ArgumentNullException(nameof(from));
                    to = to?.ToUpperInvariant() ?? throw new ArgumentNullException(nameof(to));

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

            [Description("Converts a local time in a given timezone to UTC.")]
            public static string ConvertToUtc(
    [Description("Time in local format (yyyy-MM-dd HH:mm)")] string localTime,
    [Description("Timezone ID (e.g., Asia/Kolkata)")] string timezone)
            {
                try
                {
                    var tz = TimeZoneInfo.FindSystemTimeZoneById(timezone);
                    var local = DateTime.Parse(localTime);
                    var utc = TimeZoneInfo.ConvertTimeToUtc(local, tz);
                    return $"{localTime} in {timezone} = {utc:yyyy-MM-dd HH:mm} UTC";
                }
                catch (Exception ex)
                {
                    return $"Conversion failed: {ex.Message}";
                }
            }

            [Description("Gets current weather information for a given city using Open-Meteo API.")]
            public static async Task<string> Weather(
    [Description("City name (e.g., London, Chennai)")] string city)
            {
                try
                {
                    using var client = new HttpClient();
                    var geoUrl = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(city)}&count=1";
                    var geoResponse = await client.GetStringAsync(geoUrl);
                    var geo = JsonNode.Parse(geoResponse);
                    var lat = geo?["results"]?[0]?["latitude"]?.ToString();
                    var lon = geo?["results"]?[0]?["longitude"]?.ToString();

                    if (lat is null || lon is null)
                        return "City not found.";

                    var weatherUrl = $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&current_weather=true";
                    var weatherResponse = await client.GetStringAsync(weatherUrl);
                    var weather = JsonNode.Parse(weatherResponse);
                    var temp = weather?["current_weather"]?["temperature"]?.ToString();
                    var wind = weather?["current_weather"]?["windspeed"]?.ToString();

                    return $"Current weather in {city}: {temp}°C, wind speed {wind} km/h.";
                }
                catch
                {
                    return "Failed to fetch weather.";
                }
            }

            [Description("Evaluates a math expression using MathJS API.")]
            public static async Task<string> EvaluateMath(
    [Description("Mathematical expression (e.g., 2+2*5)")] string expression)
            {
                try
                {
                    using var client = new HttpClient();
                    var url = $"https://api.mathjs.org/v4/?expr={Uri.EscapeDataString(expression)}";
                    var result = await client.GetStringAsync(url);
                    return $"{expression} = {result}";
                }
                catch
                {
                    return "Failed to evaluate expression.";
                }
            }

            [Description("Generates a random integer in the given range.")]
            public static string RandomInt(
    [Description("Minimum value (inclusive)")] int min,
    [Description("Maximum value (inclusive)")] int max)
            {
                if (min > max)
                    return "Min cannot be greater than max.";

                var rng = new Random();
                var value = rng.Next(min, max + 1);
                return $"Random number between {min} and {max}: {value}";
            }


        }
    }
}
