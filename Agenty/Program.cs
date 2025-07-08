using Agenty.Core;
using OpenAI.Chat;
using System.ComponentModel;
using System.Text.Json.Nodes;

namespace Agenty
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            Console.WriteLine("Helloworld");

            var tlReg = new ToolRegistry();
            var llm = new OpenAIClient(tlReg);
            llm.Initialize("http://127.0.0.1:1234/v1", "lm-studio", "gpt-3.5");

            tlReg.Register(Tools.Add, "add");

            var tlCallInfo = llm.GetFunctionCallResponse(new SimplePrompt("What the fuk is 3333 and 3333 together give?")).Result;
            var tlResult = tlReg.InvokeTool(tlCallInfo.FirstOrDefault());

            var res = llm.GenerateStreamingResponse(new SimplePrompt(tlResult));
            await foreach (var i in res)
            {
                Console.Write(i);
            }
        }
    }

    class SimplePrompt(string message) : IPrompt
    {
        public IEnumerable<ChatInput> Messages => new[]
        {
            new ChatInput(ChatRole.User, message)
        };
    }

    class Tools
    {
        [Description("Adds two integers and returns the result as a string.")]
        public static string Add(
            [Description("First integer to add.")] int a,
            [Description("Second integer to add.")] int b)
        {
            return (a + b).ToString();
        }
    }

}

