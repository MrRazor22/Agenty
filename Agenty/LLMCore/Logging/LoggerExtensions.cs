using Agenty.AgentCore.TokenHandling;
using Agenty.LLMCore.ChatHandling;
using Agenty.LLMCore.JsonSchema;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;

public static class LoggerExtensions
{
    private static readonly ConditionalWeakTable<Conversation, HashSet<ILogger>> _attached
        = new ConditionalWeakTable<Conversation, HashSet<ILogger>>();

    public static void AttachTo(this ILogger logger, Conversation conversation, string source = "Conversation")
    {
        var loggers = _attached.GetOrCreateValue(conversation);
        if (loggers.Contains(logger))
            return; // already attached

        loggers.Add(logger);

        conversation.OnChat += chat =>
        {
            var msg = chat.Content?.AsJSONString() ?? "<empty>";

            var level = chat.Role switch
            {
                Role.User => LogLevel.Information,
                Role.Assistant => LogLevel.Information,
                Role.Tool => LogLevel.Debug,
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


