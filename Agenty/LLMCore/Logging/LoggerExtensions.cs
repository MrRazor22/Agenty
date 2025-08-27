using Agenty.LLMCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using ILogger = Agenty.LLMCore.Logging.ILogger;

public static class LoggerExtensions
{
    public static void AttachTo(this ILogger logger, Conversation conversation)
    {
        conversation.OnChat += chat =>
        {
            var obj = chat.Content ?? (object?)chat.toolCallInfo ?? "<empty>";
            var msg = obj is string s ? s : JsonSerializer.Serialize(obj);

            logger.Log(
                chat.Role is Role.Assistant or Role.User or Role.Tool ? LogLevel.Information : LogLevel.Debug,
                nameof(Conversation),
                $"{chat.Role}: {msg}",
                chat.Role switch
                {
                    Role.User => ConsoleColor.Cyan,
                    Role.Assistant => ConsoleColor.Green,
                    Role.Tool => ConsoleColor.Yellow,
                    _ => (ConsoleColor?)null
                }
            );
        };
    }
}
