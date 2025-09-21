using Agenty.LLMCore.ChatHandling;
using Agenty.LLMCore.Messages;

namespace Agenty.AgentCore.Prompts
{
    public sealed class PromptTemplate
    {
        private readonly List<(Role Role, string Text)> _parts;

        public PromptTemplate(IEnumerable<(Role Role, string Text)> parts)
        {
            _parts = parts.ToList();
        }

        public Conversation ToConversation(Dictionary<string, string>? variables = null)
        {
            var conv = new Conversation();

            foreach (var (role, text) in _parts)
            {
                var filled = variables != null ? Format(text, variables) : text;
                conv.Add(role, new TextContent(filled));
            }

            return conv;
        }

        private static string Format(string template, Dictionary<string, string> vars)
        {
            foreach (var kv in vars)
                template = template.Replace($"{{{kv.Key}}}", kv.Value);
            return template;
        }
    }
}
