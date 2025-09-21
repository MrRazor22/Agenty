using Agenty.AgentCore.TokenHandling;
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
            var msg = chat.Content?.AsJSONString() ?? "<empty>";

            var level = chat.Role switch
            {
                Role.User => LogLevel.Information,
                Role.Assistant => LogLevel.Information,
                Role.Tool => LogLevel.Debug, // tools are often noisy
                _ => LogLevel.Debug
            };

            logger.Log(level, new EventId(chat.Role.GetHashCode(), source), $"{source}/{chat.Role}: {msg}");
        };
    }
    public static void LogUsage(this ILogger logger, TokenUsageReport report, string source = "Tokens")
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"[Tokens] {report.TotalTokens}/{report.MaxTokens} (trimmed={report.WasTrimmed})");

        foreach (var kv in report.RoleCounts)
            sb.Append($", {kv.Key}={kv.Value}");

        if (report.DroppedCount > 0)
            sb.Append($", Dropped≈{report.DroppedCount}");

        logger.LogInformation(new EventId(0, source), sb.ToString());
    }
}


