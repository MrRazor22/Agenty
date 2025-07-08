using Xunit;
using System.Text.Json.Nodes;
using Agenty.Core;
using System.Collections.Generic;
using System.Threading.Tasks;

public class AgentSdkTests
{
    private class SimplePrompt(string message) : IPrompt
    {
        public IEnumerable<ChatInput> Messages => new[]
        {
            new ChatInput(ChatRole.User, message)
        };
    }

    private static ILLMClient CreateClient(out IToolRegistry registry)
    {
        registry = new ToolRegistry();
        registry.RegisterAll([
            (Func<int[], int>)SumArray,
            (Func<string, string>)Echo,
            (Func<DayOfWeek, string>)DescribeDay
        ]);
        var client = new OpenAIClient((ToolRegistry)registry);
        client.Initialize("http://127.0.0.1:1234/v1", "lm-studio", "gpt-3.5");
        return client;
    }

    [Fact]
    public async Task Echo_ToolCall_Works()
    {
        var llm = CreateClient(out var registry);
        var prompt = new SimplePrompt("Echo 'hello world'");

        var toolCalls = await llm.GetFunctionCallResponse(prompt);
        Assert.Single(toolCalls);
        Assert.Equal("Echo", toolCalls[0].Name, ignoreCase: true);

        var result = registry.InvokeTool(toolCalls[0]);
        Assert.Contains("hello world", result, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToolCall_CaseInsensitive_Name_Matches()
    {
        var llm = CreateClient(out var registry);
        var call = new ToolCallInfo
        {
            Id = "caseTest",
            Name = "echo", // lowercase
            Parameters = new JsonObject { ["message"] = "case test" }
        };

        var result = registry.InvokeTool(call);
        Assert.Contains("case test", result, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Enum_Parameter_Tool_Works()
    {
        var llm = CreateClient(out var registry);
        var call = new ToolCallInfo
        {
            Id = "enumTest",
            Name = "DescribeDay",
            Parameters = new JsonObject { ["day"] = "Friday" }
        };

        var result = registry.InvokeTool(call);
        Assert.Contains("Friday", result);
    }

    [Fact]
    public void Null_Params_Should_Return_Error()
    {
        var llm = CreateClient(out var registry);
        var call = new ToolCallInfo
        {
            Id = "broken",
            Name = "Echo",
            Parameters = null!
        };

        var result = registry.InvokeTool(call);
        Assert.Equal("[Invalid tool call paramater JSON]", result);
    }

    [Fact]
    public async Task Streaming_Works_And_Returns_Text()
    {
        var llm = CreateClient(out _);
        var prompt = new SimplePrompt("Tell me a joke");

        await foreach (var chunk in llm.GenerateStreamingResponse(prompt))
        {
            Assert.False(string.IsNullOrEmpty(chunk));
            break; // Just verify we get any chunk
        }
    }

    [Fact]
    public void Structured_Output_Returns_Valid_Json()
    {
        var llm = CreateClient(out _);
        var prompt = new SimplePrompt("Is 9 a prime number?");
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["answer"] = new JsonObject { ["type"] = "string" },
                ["reasoning"] = new JsonObject { ["type"] = "string" }
            },
            ["required"] = new JsonArray { "answer", "reasoning" }
        };

        var json = llm.GetStructuredResponse(prompt, schema);
        Assert.True(json.ContainsKey("answer"));
        Assert.True(json.ContainsKey("reasoning"));
    }

    static int SumArray(int[] numbers) => numbers.Sum();
    static string Echo(string message = "default") => $"Echo: {message}";
    static string DescribeDay([EnumValues("Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday")] DayOfWeek day) => $"You picked: {day}";
}
