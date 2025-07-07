using Xunit;
using System.Text.Json.Nodes;
using Agenty.Core;

public class AgentSdkTests
{
    [Fact]
    public async Task Run_All_Features_Work()
    {
        IToolRegistry toolRegistry = new ToolRegistry();

        var funcs = new List<Delegate>
        {
            (Func<int[], int>)SumArray,
            (Func<string, string>)Echo,
            (Func<DayOfWeek, string>)DescribeDay
        };
        toolRegistry.RegisterAll(funcs);

        ILLMClient llm = new OpenAIClient((ToolRegistry)toolRegistry);
        llm.Initialize("http://127.0.0.1:1234/v1", "lm-studio", "gpt-3.5");
        llm.SetSystemPrompt("You are a helpful assistant using registered tools.");

        var toolCalls = await llm.GetFunctionCallResponse("Echo 'hello world'");
        foreach (var call in toolCalls)
        {
            Assert.Equal("Echo", call.Name);
            var result = toolRegistry.InvokeTool(call);
            Assert.Contains("hello world", result);
        }

        var caseCall = new ToolCallInfo
        {
            Id = "caseTest",
            Name = "echo",
            Parameters = new JsonObject { ["message"] = "case test" }
        };
        var caseResult = toolRegistry.InvokeTool(caseCall);
        Assert.Contains("case test", caseResult);

        var enumCall = new ToolCallInfo
        {
            Id = "enumTest",
            Name = "DescribeDay",
            Parameters = new JsonObject { ["day"] = "Friday" }
        };
        var enumResult = toolRegistry.InvokeTool(enumCall);
        Assert.Contains("Friday", enumResult);

        var brokenCall = new ToolCallInfo
        {
            Id = "broken",
            Name = "Echo",
            Parameters = null
        };
        var brokenResult = toolRegistry.InvokeTool(brokenCall);
        Assert.Equal("[Invalid tool call paramater JSON]", brokenResult);

        await foreach (var chunk in llm.GenerateStreamingResponse("Tell me a joke"))
        {
            Assert.False(string.IsNullOrEmpty(chunk));
            break; // Just validate at least one chunk received
        }

        var responseFormat = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["answer"] = new JsonObject { ["type"] = "string" },
                ["reasoning"] = new JsonObject { ["type"] = "string" }
            },
            ["required"] = new JsonArray { "answer", "reasoning" }
        };
        var structured = llm.GetStructuredResponse("Is 9 a prime number?", responseFormat);
        Assert.True(structured.ContainsKey("answer"));
        Assert.True(structured.ContainsKey("reasoning"));
    }

    static int SumArray(int[] numbers) => numbers.Sum();
    static string Echo(string message = "default") => $"Echo: {message}";


    static string DescribeDay([EnumValues("Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday")] DayOfWeek day) => $"You picked: {day}";
}
