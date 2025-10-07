using Agenty.LLMCore.ChatHandling;

namespace Agenty.AgentCore.TokenHandling
{

    public interface ITokenManager
    {
        // Count tokens for a whole conversation
        int CountTokens(Conversation convo);

        // Trim conversation to fit budget
        void Trim(Conversation convo);
    }


}
