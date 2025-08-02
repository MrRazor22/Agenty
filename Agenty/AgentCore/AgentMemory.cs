using Agenty.LLMCore;

namespace Agenty.AgentCore
{
    public class AgentMemory : IAgentMemory
    {
        private readonly IAgentLogger? _logger;
        public string Goal { get; set; } = string.Empty;
        public IPlan? Plan { get; set; } = null;

        public Action<string>? OnThoughtGenerated { get; set; }
        public Action<string>? OnChatHistoryUpdated { get; set; }

        public IScratchpad Thoughts => _thoughts;
        public ChatHistory ChatHistory => _chatHistory;

        private readonly Scratchpad _thoughts;
        private readonly ChatHistory _chatHistory;

        public AgentMemory(IAgentLogger? logger = null)
        {
            _thoughts = new Scratchpad(() => OnThoughtGenerated);
            _chatHistory = new ChatHistory();
            _logger = logger;
            OnThoughtGenerated = t => _logger?.Log("ScratchPad", $"{t}");
            OnChatHistoryUpdated = c => _logger?.Log("ChatHistory", $"{c}");
        }

        public void Clear()
        {
            Goal = string.Empty;
            Plan = null;
            _thoughts.Clear();
            _chatHistory.Clear();
        }
    }
}
