using Agenty.Core;
using HtmlAgilityPack;
using Microsoft.VisualStudio.TestPlatform.CommunicationUtilities;
using OpenAI.Chat;
using System.ComponentModel;
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

            var tlReg = new ToolRegistry();
            tlReg.Register(Tools.WikiSummary, "wiki");

            var llm = new OpenAIClient();
            llm.Initialize("http://127.0.0.1:1234/v1", "lm-studio", "qwen 2.5b");

            var prompt = new SimplePrompt();
            prompt.Add(ChatRole.System, """
                You are a helpful assistant. Use tools when needed.
                Use 'WikiSummary' for any known person, place, event, or topic. Do not guess or hallucinate explanations if a tool is available. Only use internal knowledge if all tools fail or return empty.
        
                Format:
                <reasoning>...</reasoning>
                <answer>...</answer>
                """);

            var tools = tlReg.GetRegisteredTools();

            while (true)
            {
                Console.Write("\n> ");
                var userInput = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(userInput)) continue;
                if (userInput.ToLower() == "exit") break;

                prompt.Add(ChatRole.User, userInput);

                while (true)
                {
                    var toolCalls = await llm.GetFunctionCallResponse(prompt, tools);
                    var call = toolCalls.FirstOrDefault();

                    if (string.IsNullOrEmpty(call?.Name))
                    {
                        Console.Write("[Assistant]: ");
                        var stream = llm.GenerateStreamingResponse(prompt);
                        await foreach (var chunk in stream)
                            Console.Write(chunk);
                        Console.WriteLine();
                        break;
                    }

                    Console.WriteLine($"\n[ToolCall] => {call}");
                    prompt.Add(ChatRole.Assistant, null, call);

                    var result = tlReg.InvokeTool(call);
                    Console.WriteLine($"[ToolResult] => {result}\n");
                    prompt.Add(ChatRole.Tool, result, call);
                }
            }
        }

    }
}

public class SimplePrompt : IPrompt
{
    private readonly List<ChatInput> _messages = new();

    public IEnumerable<ChatInput> Messages => _messages;

    public void Add(ChatRole role, string content, ToolCallInfo? toolCallInfo = null)
    {
        _messages.Add(new ChatInput(role, content, toolCallInfo));
        //Console.WriteLine($"[{role}]: {content} {toolCallInfo}");
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

