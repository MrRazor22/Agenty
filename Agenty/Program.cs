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
            var llm = new LLMCore.OpenAIClient();
            llm.Initialize("http://127.0.0.1:1234/v1", "lmstudio", "any_model");

            // Register tools
            ITools tools = new Tools();
            tools.Register(AdvancedTools.DescribePerson, UserTools.WikiSummary);

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
                    Console.WriteLine($"Error fetching tool response: {ex.Message}");
                    continue;
                }

                if (!string.IsNullOrEmpty(toolCall.AssistantMessage))
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
                    Console.WriteLine($"🔧 Tool Call Detected → {toolCall}");
                    Console.ResetColor();

                    object? result = tools.Invoke<object>(toolCall);

                    if (result is Person person)
                    {
                        Console.WriteLine($"👤 {person.Name}, {person.Age} years old, Gender: {person.Gender}, Lives in: {person.Address.Street}, {person.Address.City}, {person.Address.Country}");
                    }
                    else
                    {
                        Console.WriteLine($"📄 Result: {result}");
                    }

                    // Add tool call to chat history
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



        public class AdvancedTools
        {
            [Description("Gives a brief about the person including address and age group.")]
            public static Person DescribePerson(
                [Description("Details about the person")] Person person)
            {
                var ageGroup = person.Age < 18 ? "a minor" :
                               person.Age < 60 ? "an adult" : "a senior citizen";

                var address = $"{person.Address.Street}, {person.Address.City}, {person.Address.Country}";
                Console.WriteLine($"{person.Name} is {ageGroup}, aged {person.Age}, gender: {person.Gender}, living at {address}.");
                person.Name = "tONY";
                return person;
            }
        }
        public enum Gender
        {
            Male,
            Female,
            NonBinary,
            Other
        }
        public class Person
        {
            [Description("Full name of the person")]
            public string Name { get; set; }

            [Description("Age of the person")]
            public int Age { get; set; }

            [Description("Where the person lives")]
            public Address Address { get; set; }

            [Description("Gender of the person")]
            public Gender Gender { get; set; } // NEW
        }

        public class Address
        {
            [Description("Street name")]
            public string Street { get; set; }

            [Description("City")]
            public string City { get; set; }

            [Description("Country")]
            public string Country { get; set; }
        }


        class UserTools
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
}

