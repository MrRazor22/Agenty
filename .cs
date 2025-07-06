using OpenAI;
using OpenAI.Chat;

public class LLMEndpoint
{
    private readonly OpenAIClient _client;
    public LLMEndpoint(string baseUrl, string apiKey)
    {
        _client = new OpenAIClient(
        new OpenAIAuthentication(apiKey),
        new OpenAIClientSettings
        {
            BaseDomain = baseUrl
        });
    }
    public async Task<string> GenerateResponseAsync(string prompt)
    {
        var chatRequest = new ChatRequest
        {
            Messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.User, prompt)
            }
        };
        var response = await _client.Chat.CreateAsync(chatRequest);
        return response.Choices[0].Message.Content;
    }
}