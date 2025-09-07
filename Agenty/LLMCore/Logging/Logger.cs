using Microsoft.Extensions.Logging;
namespace Agenty.LLMCore.Logging
{
    public interface ILogger
    {
        void Log(string message);
        void Log(LogLevel level, string source, string message);
        void Log(LogLevel level, string source, string message, Exception exception);
        void Log(LogLevel level, string source, string message, ConsoleColor? colorOverride = null);
    }

    public class ConsoleLogger : ILogger
    {
        private readonly LogLevel _minLevel;
        private readonly string _defaultSource = "Default";
        private readonly LogLevel _defaultLevel = LogLevel.Information;

        // Constructor to set minimum log level (default to Information)
        public ConsoleLogger(LogLevel minLevel = LogLevel.Information)
        {
            _minLevel = minLevel;
        }
        public void Log(string message)
        {
            Log(_defaultLevel, _defaultSource, message);
        }

        public void Log(LogLevel level, string source, string message)
        {
            if (level < _minLevel)
                return; // Skip logging below minimum level

            var originalColor = Console.ForegroundColor;
            Console.ForegroundColor = GetColor(level, source);
            Console.WriteLine($"[{level}] [{source}] {message}");
            Console.ForegroundColor = originalColor;
        }

        public void Log(LogLevel level, string source, string message, Exception exception)
        {
            if (level < _minLevel)
                return; // Skip logging below minimum level

            var originalColor = Console.ForegroundColor;
            Console.ForegroundColor = GetColor(level, source);
            Console.WriteLine($"[{level}] [{source}] {message}");
            if (exception != null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Exception: {exception}");
            }
            Console.ForegroundColor = originalColor;
        }

        public void Log(LogLevel level, string source, string message, ConsoleColor? colorOverride = null)
        {
            if (level < _minLevel) return;

            var originalColor = Console.ForegroundColor;
            Console.ForegroundColor = colorOverride ?? GetColor(level, source);
            Console.WriteLine($"[{level}] [{source}] {message}");
            Console.ForegroundColor = originalColor;
        }

        private ConsoleColor GetColor(LogLevel level, string source)
        {
            // Hash source for stable pseudo-random
            int hash = Math.Abs(source.GetHashCode());

            return level switch
            {
                LogLevel.Trace => (hash % 2 == 0 ? ConsoleColor.DarkGray : ConsoleColor.Gray),
                LogLevel.Debug => (hash % 2 == 0 ? ConsoleColor.DarkCyan : ConsoleColor.Cyan),
                LogLevel.Information => (hash % 2 == 0 ? ConsoleColor.DarkGreen : ConsoleColor.Green),
                LogLevel.Warning => (hash % 2 == 0 ? ConsoleColor.DarkYellow : ConsoleColor.Yellow),
                LogLevel.Error => (hash % 2 == 0 ? ConsoleColor.DarkRed : ConsoleColor.Red),
                LogLevel.Critical => (hash % 2 == 0 ? ConsoleColor.DarkMagenta : ConsoleColor.Magenta),
                _ => (hash % 2 == 0 ? ConsoleColor.White : ConsoleColor.Gray),
            };
        }
    }
}
