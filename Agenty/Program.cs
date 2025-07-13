using Agenty.AgentCore;
using Agenty.LLMCore;
using HtmlAgilityPack;
using Microsoft.VisualStudio.TestPlatform.CommunicationUtilities;
using OpenAI;
using OpenAI.Chat;
using System.ComponentModel;
using System.Data;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Agenty
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            Console.WriteLine("== Chatbot Started ==\nType 'exit' to quit.");

            var agent = new Agent("WikipediaAgent")
                .WithModel("http://localhost:1234/v1", "lm-studio", "qwen:7b")
                .WithTools([Tools.WikiSummary]);
            //.WithGoal("""
            //            You are a helpful chatbot who tries to solve the user's query as accurately as possible.
            //            You can use the 'WikiSummary' tool for any known person, place, event, or topic. 
            //            Do not rely on your own knowledge unless the tool fails or gives an empty or useless result.
            //            After receiving tool output, always compare it directly to the user's original query. 
            //            If the result only partially matches, or lacks detail expected from the user's wording, 
            //            re-call the tool with improved input.
            //            You must not stop at the first non-empty result. You must judge whether it is truly sufficient, 
            //            based on the user's actual intent. 
            //        """) 

            while (true)
            {
                Console.Write("\n> ");
                string input = Console.ReadLine()?.Trim() ?? string.Empty;
                if (input.Equals("exit", StringComparison.OrdinalIgnoreCase))
                    break;

                await agent.Execute(input);
            }
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

