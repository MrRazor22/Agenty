using System;

public interface ILLMClient
{
    public ILLMClient(string, string);
    public string GenerateResponseAsync(string, string)
}
