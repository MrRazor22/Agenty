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
            var llm = new LLMCore.OpenAIClient();
            llm.Initialize("http://localhost:1234/v1", "lm-studio", "qwen:7b");

            var registry = new ToolRegistry();
            registry.RegisterAll(Deliver, DeliverAdvanced);

            var executor = new ToolExecutor(registry);

            //var prompt = new Prompt("Please plan a delivery to Bangalore city, in two addresses, [ayapakkam, ambattur, Bangalore 600072] and [ayappa nagar, ambattur, Bangalore 600077] for 3 items, high priority");
            var prompt = new Prompt(@"
Schedule an urgent delivery with these details:

Delivery ID: 7c8e1f91-e7b4-4d21-8470-abc123def456  
Main destination: '742, Evergreen Terrace', SpringField, 62704  
Alternate destinations:  
- '12, Baker Street', London, 123456  
- 'Sector 21B', Chandigarh,   
- '' (left blank intentionally)  

Recipient: Mr. Homer J. Simpson, simpson@springmail.com, Phone: 1234567890 (WhatsApp: no)  
Items: [donuts, DuffBeer, radioactive_isotope]  
Priority: Urgent  
Note: Handle with extreme caution!!!  
Schedule date: 2025-12-25  
");

            var tools = registry.GetRegisteredTools();
            var response = await llm.GetFunctionCallResponse(prompt, tools);

            if (response.ToolCalls.Count == 0)
            {
                Console.WriteLine("Assistant Message: " + response.AssistantMessage);
                return;
            }

            foreach (var call in response.ToolCalls)
            {
                Console.WriteLine($"Tool Call from model:\n{call}\n");
                var result = executor.InvokeTool(call);
                Console.WriteLine($"Tool Result: {result}");
            }
        }

        [Description("Handles a delivery request")]
        public static string Deliver([Description("The request info")] DeliveryRequest request)
        {
            return $"Delivered to {request.Destination[0].City} with {request.ItemIds.Length} items (Priority: {request.Priority})";
        }

        [Description("Handles a super complex delivery")]
        public static string DeliverAdvanced([Description("Full delivery info")] ComplexDelivery input)
        {
            return $"Delivery ID {input.Id} to {input.MainDestination.City} and {input.AlternateDestinations?.Count ?? 0} alternates. " +
                   $"Priority: {input.Priority}, Schedule: {input.Schedule ?? "None"}, Items: {input.Items?.Length}";
        }

    }
}
public class ComplexDelivery
{
    [Description("Unique delivery ID")]
    public string Id { get; set; }

    [Description("Delivery schedule date in 'YYYY-MM-DD' format (e.g. '2025-07-21')")]
    public string? Schedule { get; set; }

    [Description("Main destination address")]
    public Address MainDestination { get; set; }

    [Description("Optional backup addresses")]
    public List<Address>? AlternateDestinations { get; set; }

    [Description("Priority level")]
    [EnumValues("Low", "Normal", "Urgent")]
    public string Priority { get; set; }

    [Description("Customer contact details")]
    public ContactInfo Contact { get; set; }

    [Description("Any special instructions")]
    public string? Instructions { get; set; }

    [Description("Item IDs")]
    public string[] Items { get; set; }
}

public class ContactInfo
{
    [Description("Full name of recipient")]
    public string Name { get; set; }

    [Description("Email ID")]
    public string Email { get; set; }

    [Description("Is WhatsApp available on this number?")]
    public bool? WhatsApp { get; set; }

    [Description("Mobile number (10 digits)")]
    public int Phone { get; set; }
}

public class DeliveryRequest
{
    [Description("Destination address")]
    public List<Address> Destination { get; set; }

    [Description("Contact name")]
    public string Contact { get; set; }

    [Description("Delivery priority")]
    [EnumValues("Low", "Medium", "High")]
    public string Priority { get; set; }

    [Description("Special notes")]
    public string? Notes { get; set; }

    [Description("Item IDs")]
    public int[] ItemIds { get; set; }
}

public class Address
{
    [Description("only Street names like wallstreet, kumar nager, amman street etc")]
    public string Street { get; set; }

    [Description("City names like chennai, new york, mumbai etc")]
    public string City { get; set; }

    [Description("Zip code (6-digit integer only, like 600077)")]
    public int? ZipCode { get; set; }
}



//public static async Task Main(string[] args)
//{
//    Console.WriteLine("== Chatbot Started ==\nType 'exit' to quit.");

//    var agent = new Agent("WikipediaAgent")
//        .WithModel("http://localhost:1234/v1", "lm-studio", "qwen:7b")
//        .WithTools([Tools.WikiSummary]);
//    //.WithGoal("""
//    //            You are a helpful chatbot who tries to solve the user's query as accurately as possible.
//    //            You can use the 'WikiSummary' tool for any known person, place, event, or topic. 
//    //            Do not rely on your own knowledge unless the tool fails or gives an empty or useless result.
//    //            After receiving tool output, always compare it directly to the user's original query. 
//    //            If the result only partially matches, or lacks detail expected from the user's wording, 
//    //            re-call the tool with improved input.
//    //            You must not stop at the first non-empty result. You must judge whether it is truly sufficient, 
//    //            based on the user's actual intent. 
//    //        """) 

//    while (true)
//    {
//        Console.Write("\n> ");
//        string input = Console.ReadLine()?.Trim() ?? string.Empty;
//        if (input.Equals("exit", StringComparison.OrdinalIgnoreCase))
//            break;

//        await agent.Execute(input);
//    }
//}
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

