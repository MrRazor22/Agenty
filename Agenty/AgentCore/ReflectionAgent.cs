using Agenty.LLMCore;
using Agenty.LLMCore.Providers.OpenAI;
using System.Text.Json.Nodes;

namespace Agenty.AgentCore
{
    public sealed class ReflectionAgent : IAgent
    {
        private ILLMClient _llm = null!;
        private ToolCoordinator _coord = null!;
        private readonly IToolRegistry _tools = new ToolRegistry();

        public static ReflectionAgent Create() => new();
        private ReflectionAgent() { }

        public ReflectionAgent WithLLM(string baseUrl, string apiKey, string model = "any_model")
        {
            _llm = new OpenAILLMClient();
            _llm.Initialize(baseUrl, apiKey, model);
            _coord = new ToolCoordinator(_llm, _tools);
            return this;
        }

        public ReflectionAgent WithTools<T>() { _tools.RegisterAll<T>(); return this; }
        public ReflectionAgent WithTools(params Delegate[] fns) { _tools.Register(fns); return this; }


        enum Verdict { Yes, No }
        record AnswerGrade(Verdict verdict, string explanation);

        public async Task<string> ExecuteAsync(string goal, int maxRounds = 50)
        {
            var chat = new Conversation().Add(Role.System,
                                            "You are a concise QA assistant. Answer in <=3 sentences.")
                                        .Add(Role.User, goal);

            for (int round = 0; round < maxRounds; round++)
            {
                // GATE 1: Intent — either we already can answer (Final) OR we need a tool
                var response = await _llm.GetResponse(chat);
                Console.WriteLine("========================================================");
                Console.WriteLine($"Response: {response}");
                Console.WriteLine("========================================================");

                var grade = await _coord.GetStructuredResponse<AnswerGrade>(
    new Conversation()
        .Add(Role.System, @"You are grading whether the ASSISTANT RESPONSE appropriately satisfies the USER REQUEST.

                            Verdict rules:
                            - Yes → The response either:
                              (a) fully fulfills the user request, OR
                              (b) cannot fulfill it due to lack of data/system limitation, but clearly explains why with reasonable justification (not just vague statements).
                            - No => The response fails to address the request, gives incorrect/misleading information, ignores part of the request, or refuses without explanation.

                            Always provide an explanation of your reasoning along with the verdict.")
                                    .Add(Role.User, $"USER REQUEST: {goal}\nASSISTANT RESPONSE: {response}")
                            );

                Console.WriteLine($"Score: {grade.verdict}");
                Console.WriteLine($"explanation: {grade.explanation}");

                if (grade.verdict == Verdict.Yes) return response;
                chat.Add(Role.User, grade.explanation);
            }

            chat.Add(Role.User, "Max rounds reached. Return the best final answer now as plain text.");
            return await _llm.GetResponse(chat);
        }
    }
}
