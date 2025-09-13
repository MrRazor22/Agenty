using Agenty.LLMCore;


namespace Agenty.AgentCore.TokenHandling
{
    public interface ITokenManager
    {
        void Trim(Conversation convo, int maxTokens);
    }
    /// <summary>
    /// Default policy:
    /// - Keep all System + Backstory (never trimmed)
    /// - Drop Tool + temporary chats first
    /// - Then apply sliding window on User/Assistant
    /// </summary> 
    public sealed class DefaultTokenManager : ITokenManager
    {
        private readonly ITokenizer _tokenizer;

        public DefaultTokenManager(ITokenizer tokenizer)
        {
            _tokenizer = tokenizer ?? throw new ArgumentNullException(nameof(tokenizer));
        }

        public void Trim(Conversation convo, int maxTokens)
        {
            int count() => _tokenizer.CountTokens(convo.ToString(ChatFilter.All));

            if (count() <= maxTokens) return;

            // Keep System messages
            var system = convo.Where(c => c.Role == Role.System).ToList();

            // Drop Tool + temp
            convo.RemoveAll(c => c.Role == Role.Tool || c.IsTemporary);
            if (count() <= maxTokens) return;

            // Sliding window on User + Assistant
            var core = convo.Where(c => c.Role == Role.User || c.Role == Role.Assistant).ToList();
            while (count() > maxTokens && core.Count > 1)
            {
                var oldest = core.First();
                convo.Remove(oldest);
                core.RemoveAt(0);
            }

            // Reinsert system at the top if lost
            foreach (var msg in system)
            {
                if (!convo.Contains(msg))
                    convo.Insert(0, msg);
            }
        }
    }

}
