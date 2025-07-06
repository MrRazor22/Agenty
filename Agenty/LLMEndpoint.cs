using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
public class OpenAILLMClient
{
    private readonly OpenAIClient _client;
    private ChatClient _chatClient;
    public OpenAILLMClient(string baseUrl, string apiKey, string modelName = "any_model")
    {
        _client = new(
            credential: new ApiKeyCredential(apiKey),
            options: new OpenAIClientOptions()
            {
                Endpoint = new Uri(baseUrl)
            }
        );

        _chatClient = _client.GetChatClient(modelName);
    }
    public async Task<string> GenerateResponseAsync(string prompt)
    {
        var response = await _chatClient.CompleteChatAsync(new[]
        {
            ChatMessage.CreateUserMessage(prompt)
        });

        var contentParts = response.Value.Content;
        var textContent = string.Join("", contentParts.Select(part => part.Text));
        return textContent;
    }
}