using Agenty;
using OpenAI.Chat;
using System.ComponentModel;

public class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("== AGENT TOOL TESTS ==");

        ToolRegistry toolRegistry = new ToolRegistry();
        var llm = new OpenAILLMClient(toolRegistry);
        llm.Init("http://127.0.0.1:1234/v1", "lm-studio");

        // Run all tool test cases
        var tools = ToolTests.RegisterAllTools(toolRegistry);

        RunPrompt(llm, "What is the capital of France?"); // no tools
        RunPrompt(llm, "What's the weather in Paris?", tools); // normal call
        //RunPrompt(llm, "What's the weather in Paris in kelvin?", tools); // invalid enum
        //RunPrompt(llm, "What's the weather?", tools); // missing required
        //RunPrompt(llm, "What's the weather in 123?", tools); // wrong type
        //RunPrompt(llm, "What's the forecast in Chennai?", tools); // multiple tools
        //RunPrompt(llm, "Call WeAtHeR please", tools); // case-insensitive
        //RunPrompt(llm, "Get weather for Tokyo and include wind speed", tools); // extra arg
        //RunPrompt(llm, "Get weather for Cairo with default unit", tools); // optional param test

        Console.WriteLine("== DONE ==");
    }

    public static void RunPrompt(OpenAILLMClient llm, string prompt, List<ChatTool>? tools = null)
    {
        Console.WriteLine($"\n[Prompt] {prompt}");
        string output = llm.GenerateResponseAsync(prompt, tools).Result;
        Console.WriteLine($"[Response] {output}");
    }
}

public static class ToolTests
{
    public static List<ChatTool> RegisterAllTools(ToolRegistry registry)
    {
        var list = new List<ChatTool>();
        list.Add(registry.RegisterTool(Weather));
        list.Add(registry.RegisterTool(WeatherForecast));
        return list;
    }

    [Description("Get the weather for a given location.")]
    public static string Weather(
        [Description("Why you want to use this tool? breifly specify your reason here")] string reason,
        [Description("City and state, e.g., Boston, MA")] string location,
        [Description("Temperature unit")][EnumValues("celsius", "fahrenheit")] string unit = "celsius"
    )
    {
        Console.WriteLine($"Reason: {reason} | The weather in {location} is 22 degrees {unit}.");
        return $"Reason: {reason} | The weather in {location} is 22 degrees {unit}.";
    }

    [Description("Get the forecast for a given location.")]
    public static string WeatherForecast(
        [Description("City and state")] string location
    )
    {
        return $"The forecast for {location} is sunny for 3 days.";
    }
}
