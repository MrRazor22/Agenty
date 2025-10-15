using Agenty;
using Agenty.AgentCore;
using Agenty.LLMCore.ChatHandling;
using Agenty.LLMCore.JsonSchema;
using Agenty.LLMCore.Messages;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

public static class LoggerExtensions
{
    private static readonly ConditionalWeakTable<Conversation, HashSet<ILogger>> _attached =
        new ConditionalWeakTable<Conversation, HashSet<ILogger>>();

    public static void AttachTo(this ILogger logger, Conversation conversation, string source = null)
    {
        var loggers = _attached.GetOrCreateValue(conversation);
        if (loggers.Contains(logger)) return;
        loggers.Add(logger);

        conversation.OnChat += chat =>
        {
            var readable = AsReadable(chat.Content);
            var role = chat.Role.ToString().PadRight(10);

            var step = StepContext.Current.Value;
            var prefix = string.IsNullOrEmpty(step)
                            ? role
                            : $"{step,-20} | Role: {role}";

            logger.LogInformation($"{prefix} =>\n{readable}\n");
        };

    }

    private static string AsReadable(IMessageContent content)
    {
        if (content == null)
            return "<empty>";

        if (content is TextContent txt && !string.IsNullOrWhiteSpace(txt.Text))
            return txt.Text.Trim(); // ← don’t replace newlines

        // if JSON, pretty print it
        try
        {
            var json = content.ToPrettyJson();
            var parsed = JsonConvert.DeserializeObject(json);
            if (parsed != null)
                return JsonConvert.SerializeObject(parsed, Formatting.Indented);
            return json;
        }
        catch
        {
            return content.ToString() ?? "<unknown>";
        }
    }
}
