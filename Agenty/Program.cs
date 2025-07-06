// See https://aka.ms/new-console-template for more information


using Agenty;
using OpenAI.Chat;
using System.ComponentModel;

public class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("Hello, World!");

        var llm = new OpenAILLMClient("http://127.0.0.1:1234/v1", "lm-studio");

        string output = llm.GenerateResponseAsync("What is the capital of France?").Result;
        Console.WriteLine(output);

        ToolRegistry toolRegistry = new ToolRegistry();
        var weathertool = toolRegistry.RegisterTool(Weather);

        output = llm.GenerateResponseAsync("What is the weather in France?", new List<ChatTool> { weathertool }).Result;
        Console.WriteLine(output);
    }

    [Description("Get the weather for a given location.")]
    public static string Weather(
       [Description("City and state, e.g., Boston, MA")] string location,
       [Description("Temperature unit")][EnumValues("celsius", "fahrenheit")] string unit
    )
    {
        return $"The weather in {location} is 75 degrees {unit}.";
    }
}




