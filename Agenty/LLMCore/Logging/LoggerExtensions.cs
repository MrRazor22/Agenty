using Agenty.LLMCore.ChatHandling;
using Agenty.LLMCore.JsonSchema;
using Microsoft.Extensions.Logging;
using System.Text.Json;

public static class LoggerExtensions
{
    public static void AttachTo(this ILogger logger, Conversation conversation, string source = "Conversation")
    {
        conversation.OnChat += chat =>
        {
            var obj = chat.Content ?? (object?)chat.ToolCalls ?? "<empty>";
            var msg = obj is string s ? s : obj.AsJSONString();

            var level = chat.Role switch
            {
                Role.User => LogLevel.Information,
                Role.Assistant => LogLevel.Information,
                Role.Tool => LogLevel.Information,
                _ => LogLevel.Debug
            };

            logger.Log(level, new EventId(0, source), $"{source}/{chat.Role}: {msg}");
        };
    }
}


