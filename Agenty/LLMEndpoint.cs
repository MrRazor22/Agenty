using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
public class OpenAILLMClient
{
    private readonly OpenAIClient _client;
    private ChatClient _chatClient;
    List<ChatMessage> _messages;

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
        _messages = new();
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

    public async Task<string> GenerateResponseAsync(string prompt, List<ChatTool> chatTools)
    {
        _messages.Add(new UserChatMessage(prompt));

        ChatCompletionOptions options = new();
        foreach (var tool in chatTools)
            options.Tools.Add(tool);

        var response = await _chatClient.CompleteChatAsync(_messages, options);

        _messages.Add(new AssistantChatMessage(response));

        var result = response.Value;

        if (result.ToolCalls.Count > 0)
        {
            foreach (var toolCall in result.ToolCalls)
            {
                var name = toolCall.FunctionName;
                var args = toolCall.FunctionArguments;

                return $"[Tool call] Name: {name}, Args: {args}";
            }
        }

        return string.Join("", result.Content?.Select(p => p.Text) ?? []);
    }
}