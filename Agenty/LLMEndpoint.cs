using Agenty;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
public class OpenAILLMClient(ToolRegistry toolRegistry)
{
    private OpenAIClient _client;
    private ChatClient _chatClient;
    List<ChatMessage> _messages = new();

    public void Init(string baseUrl, string apiKey, string modelName = "any_model")
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

    public async Task<string> GenerateResponseAsync(string prompt, List<ChatTool> chatTools)
    {
        if (chatTools == null || chatTools.Count == 0)
            return "No Tools registered";
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
                var argsJson = toolCall.FunctionArguments.ToString();

                var toolResult = toolRegistry.InvokeTool(name, argsJson) ?? "[null result]";
                _messages.Add(new ToolChatMessage(toolCall.Id, toolResult));

                // Continue chat after tool call
                var followUp = await _chatClient.CompleteChatAsync(_messages, options);
                var followUpResult = followUp.Value;
                _messages.Add(new AssistantChatMessage(followUpResult));

                return string.Join("", followUpResult.Content?.Select(p => p.Text) ?? []);
            }
        }

        return string.Join("", result.Content?.Select(p => p.Text) ?? []);
    }
}