using Agenty.AgentCore;
using Agenty.LLMCore;
using HtmlAgilityPack;
using Microsoft.VisualStudio.TestPlatform.CommunicationUtilities;
using OpenAI;
using OpenAI.Chat;
using System;
using System.ComponentModel;
using System.Data;
using System.Net;
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
            Console.WriteLine("== chatbot started ==\ntype 'exit' to quit.");

            var agent = new Agent("wikipediaagent")
                .WithModel("http://localhost:1234/v1", "lm-studio", "qwen:7b")
                .WithTools([Tools.WikiSummary]);

            while (true)
            {
                Console.WriteLine("\n> ");
                string input = Console.ReadLine()?.Trim() ?? string.Empty;
                if (input.Equals("exit", StringComparison.OrdinalIgnoreCase))
                    break;

                await agent.Execute(input);
            }
        }
    }

    class Tools
    {
        [Description("Gets a summary of a Wikipedia topic.")]
        public static string WikiSummary(
         [Description("Title of the Wikipedia article")] string topic)
        {
            using var client = new HttpClient();

            try
            {
                // Search for the best matching article title
                var searchUrl = $"https://en.wikipedia.org/w/api.php?action=query&list=search&srsearch={Uri.EscapeDataString(topic)}&format=json";
                var searchJson = client.GetStringAsync(searchUrl).Result;
                var searchObj = JsonNode.Parse(searchJson);
                var title = searchObj?["query"]?["search"]?[0]?["title"]?.ToString();

                if (string.IsNullOrWhiteSpace(title))
                    return "No matching Wikipedia article found.";

                // Get summary using the resolved title
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
    }
}

