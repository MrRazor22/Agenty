using Microsoft.Extensions.Logging;
using System;

namespace Agenty.LLMCore.Logging
{
    public class ConsoleLogger : ILogger
    {
        private readonly string _category;
        private readonly LogLevel _minLevel;

        public ConsoleLogger(string category = "Default", LogLevel minLevel = LogLevel.Information)
        {
            _category = category;
            _minLevel = minLevel;
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel >= _minLevel;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var message = formatter(state, exception);

            var originalColor = Console.ForegroundColor;
            Console.ForegroundColor = GetColor(logLevel, _category);

            Console.WriteLine("[" + logLevel + "] [" + _category + "] " + message);

            if (exception != null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Exception: " + exception);
            }

            Console.ForegroundColor = originalColor;
        }

        private ConsoleColor GetColor(LogLevel level, string source)
        {
            int hash = Math.Abs(source.GetHashCode());

            // Replaced switch expression with classic switch
            switch (level)
            {
                case LogLevel.Trace:
                    return (hash % 2 == 0 ? ConsoleColor.DarkGray : ConsoleColor.Gray);
                case LogLevel.Debug:
                    return (hash % 2 == 0 ? ConsoleColor.DarkCyan : ConsoleColor.Cyan);
                case LogLevel.Information:
                    return (hash % 2 == 0 ? ConsoleColor.DarkGreen : ConsoleColor.Green);
                case LogLevel.Warning:
                    return (hash % 2 == 0 ? ConsoleColor.DarkYellow : ConsoleColor.Yellow);
                case LogLevel.Error:
                    return (hash % 2 == 0 ? ConsoleColor.DarkRed : ConsoleColor.Red);
                case LogLevel.Critical:
                    return (hash % 2 == 0 ? ConsoleColor.DarkMagenta : ConsoleColor.Magenta);
                default:
                    return ConsoleColor.Gray;
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new NullScope();
            public void Dispose() { }
        }
    }
}
