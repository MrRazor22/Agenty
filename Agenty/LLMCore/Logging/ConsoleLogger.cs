using Microsoft.Extensions.Logging;

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

        public IDisposable BeginScope<TState>(TState state) => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= _minLevel;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var message = formatter(state, exception);

            var originalColor = Console.ForegroundColor;
            Console.ForegroundColor = GetColor(logLevel, _category);

            Console.WriteLine($"[{logLevel}] [{_category}] {message}");

            if (exception is not null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Exception: {exception}");
            }

            Console.ForegroundColor = originalColor;
        }

        private ConsoleColor GetColor(LogLevel level, string source)
        {
            int hash = Math.Abs(source.GetHashCode());

            return level switch
            {
                LogLevel.Trace => (hash % 2 == 0 ? ConsoleColor.DarkGray : ConsoleColor.Gray),
                LogLevel.Debug => (hash % 2 == 0 ? ConsoleColor.DarkCyan : ConsoleColor.Cyan),
                LogLevel.Information => (hash % 2 == 0 ? ConsoleColor.DarkGreen : ConsoleColor.Green),
                LogLevel.Warning => (hash % 2 == 0 ? ConsoleColor.DarkYellow : ConsoleColor.Yellow),
                LogLevel.Error => (hash % 2 == 0 ? ConsoleColor.DarkRed : ConsoleColor.Red),
                LogLevel.Critical => (hash % 2 == 0 ? ConsoleColor.DarkMagenta : ConsoleColor.Magenta),
                _ => ConsoleColor.Gray,
            };
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
