using System;

public interface ILLMClient
{
    public void Initialize(string url, string apiKey);
    public string GenerateResponseAsync(string prompt);
}
