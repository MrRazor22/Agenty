namespace Agenty.LLMCore
{
    public enum Role { System, Assistant, User, Tool }
    public record Chat(Role Role, string? Content, ToolCall? toolCallInfo = null);
    public class Conversation : List<Chat>
    {
        public event Action<Chat>? OnChat;
        public Conversation Add(Role role, string? content = null, ToolCall? tool = null)
        {
            var chat = new Chat(role, content, tool);
            Add(chat);
            OnChat?.Invoke(chat);
            //string toolInfo = tool != null ? $" | ToolCall: {tool}" : "";
            //Console.WriteLine($"[Conversation] Added chat - Role: {role}, Content: {content}{toolInfo}");
            return this;
        }
        public static Conversation Clone(Conversation original)
        {
            var copy = new Conversation();
            foreach (var message in original)
            {
                copy.Add(message.Role, message.Content, message.toolCallInfo);
            }
            return copy;
        }
    }
}
