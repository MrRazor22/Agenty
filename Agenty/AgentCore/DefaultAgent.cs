using Agenty.LLMCore;
using Agenty.LLMCore.Providers.OpenAI;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Agenty.AgentCore
{
    public enum StepAction
    {
        TOOL_NEEDED,
        COMPLETE
    }
    public record ReActStep(string Thought, StepAction Action, string? Response);

    public class DefaultAgent : IAgent
    {
        private ILLMClient _llm = null!;
        private ToolCoordinator _toolCoordinator = null!;
        private IToolRegistry _toolRegistry = new ToolRegistry();

        private DefaultAgent() { }
        public static DefaultAgent Create() => new DefaultAgent();

        public DefaultAgent WithLLM(string baseUrl, string apiKey, string model = "any_model")
        {
            _llm = new OpenAILLMClient();
            _llm.Initialize(baseUrl, apiKey, model);
            _toolCoordinator = new ToolCoordinator(_llm, _toolRegistry);
            return this;
        }

        public DefaultAgent WithTools<T>() { _toolRegistry.RegisterAll<T>(); return this; }
        public DefaultAgent WithTools(params Delegate[] tools) { _toolRegistry.Register(tools); return this; }

        private string StructuredSystemPrompt => $@"
You are a JSON-only ReAct agent. You must NEVER output XML tags or free text. 
Each turn you output EXACTLY ONE JSON object of ONE of these two kinds:

1) Step JSON (reasoning/progress):
{{
  ""thought"": ""string — your reasoning about what to do next"",
  ""action"": ""TOOL_NEEDED"" | ""COMPLETE"",
  ""response"": null | ""final answer if action == COMPLETE""
}}
Rules:
- If ""action"" == ""TOOL_NEEDED"": ""response"" MUST be null. Do not answer yet.
- If ""action"" == ""COMPLETE"": ""response"" MUST be a full natural-language answer. No tool calls in this turn.

2) ToolCall JSON (only when the system explicitly asks for it):
{{
  ""name"": ""<ToolName>"",
  ""arguments"": {{ ... }},
  ""id"": ""<monotonic string id starting at 0>""
}}
Rules:
- Include exactly one tool call per object.
- Do NOT include ""thought"" or ""response"" in ToolCall JSON.
- Use only the tools listed below.
- Never invent observations; only the system provides them.

System behavior:
- After you send Step JSON with ""action"":""TOOL_NEEDED"", the system will immediately ask:
  {{ ""request"": ""TOOL_CALL_JSON"" }}
  You must then reply with a single ToolCall JSON object.
- After tool execution, the system will send:
  {{ ""observation"": <tool_result_json> }}
  Use that observation in your next Step JSON.
- If your JSON is invalid, the system will send:
  {{ ""error"": ""<what is wrong>"" }}
  You must fix and resend ONLY the requested JSON object.

Goal handling:
- Prefer tools when they apply; do not answer from memory if a tool is available for the task.
- Handle multi-step goals sequentially: Step JSON → ToolCall JSON → observation → next Step JSON … until COMPLETE.

Available tools (names & schemas):
<<<TOOLS_START>>>
{{TOOLS}}
<<<TOOLS_END>>>

Correct examples

Example A (with tool)
User: ""What's the weather in Paris?""
Assistant (Step JSON):
{{""thought"":""I should call the weather tool for Paris."",""action"":""TOOL_NEEDED"",""response"":null}}
System:
{{""request"":""TOOL_CALL_JSON""}}
Assistant (ToolCall JSON):
{{""name"":""GetCurrentWeather"",""arguments"":{{""city"":""Paris"",""temperatureUnit"":""Celsius""}},""id"":""0""}}
System:
{{""observation"":{{""temperature"":22,""unit"":""C""}}}}
Assistant (Step JSON):
{{""thought"":""I have the temperature; I can answer."",""action"":""COMPLETE"",""response"":""The weather in Paris is 22 °C.""}}

Example B (no tool needed)
User: ""What is 2 + 2?""
Assistant (Step JSON):
{{""thought"":""This can be answered directly."",""action"":""COMPLETE"",""response"":""4""}}

Common mistakes (do NOT do these)
- Mixing Step JSON and ToolCall JSON in one object.
- Returning ""response"" when action==""TOOL_NEEDED"".
- Emitting ""observation"" yourself.
- Adding extra fields not defined above.

";

        public async Task<string> ExecuteAsync(string goal, int maxRounds = 10)
        {
            var chat = new Conversation()
                .Add(Role.System, StructuredSystemPrompt)
                .Add(Role.User, $"Goal: {goal}");

            for (int round = 1; round <= maxRounds; round++)
            {
                var step = await _toolCoordinator.GetStructuredResponse<ReActStep>(chat);
                if (step == null) break;

                Console.WriteLine($"[THOUGHT] {step.Thought}");
                chat.Add(Role.Assistant, step.Thought);

                switch (step.Action)
                {
                    case StepAction.TOOL_NEEDED:
                        var toolCall = await _toolCoordinator.GetToolCall(chat, true);
                        if (toolCall == null)
                        {
                            chat.Add(Role.User, "Invalid tool call. Continue reasoning.");
                            continue;
                        }

                        Console.WriteLine($"[TOOL CALL] {toolCall.Name}");
                        var obs = await _toolCoordinator.HandleToolCall(toolCall);
                        chat.Add(Role.Tool, obs, toolCall);
                        Console.WriteLine($"[OBSERVATION] {obs}");
                        break;

                    case StepAction.COMPLETE:
                        if (!string.IsNullOrWhiteSpace(step.Response))
                        {
                            Console.WriteLine($"[FINAL RESPONSE] {step.Response}");
                            return step.Response;
                        }
                        chat.Add(Role.User, "Final answer missing. Provide full response.");
                        break;
                }
            }

            Console.WriteLine("[WARN] Max rounds reached. Returning fallback response.");
            chat.Add(Role.User, "Provide final answer now.");
            return await _llm.GetResponse(chat);
        }
    }

}
