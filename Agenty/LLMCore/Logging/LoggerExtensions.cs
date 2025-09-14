using Agenty.LLMCore;
using Agenty.LLMCore.JsonSchema;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using IDefaultLogger = Agenty.LLMCore.Logging.IDefaultLogger;

public static class LoggerExtensions
{
    public static void AttachTo(this IDefaultLogger logger, Conversation conversation, string source = "Conversation")
    {
        conversation.OnChat += chat =>
        {
            var obj = chat.Content ?? (object?)chat.ToolCalls ?? "<empty>";
            var msg = obj is string s ? s : obj.AsJSONString();

            logger.Log(
                chat.Role is Role.Assistant or Role.User or Role.Tool ? LogLevel.Information : LogLevel.Debug,
                $"{source}/{chat.Role}",
                msg,
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

