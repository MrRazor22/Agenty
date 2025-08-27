using Microsoft.Extensions.Logging;
namespace Agenty.LLMCore.Logging
{
    public interface ILogger
    {
        void Log(LogLevel level, string source, string message);
        void Log(LogLevel level, string source, string message, Exception exception);
        void Log(LogLevel level, string source, string message, ConsoleColor? colorOverride = null);
    }

    public class ConsoleLogger : ILogger
    {
        private readonly LogLevel _minLevel;

        // Constructor to set minimum log level (default to Information)
        public ConsoleLogger(LogLevel minLevel = LogLevel.Information)
        {
            _minLevel = minLevel;
        }

        public void Log(LogLevel level, string source, string message)
        {
            if (level < _minLevel)
                return; // Skip logging below minimum level

            var originalColor = Console.ForegroundColor;
            Console.ForegroundColor = GetColor(level);
            Console.WriteLine($"[{level}] [{source}] {message}");
            Console.ForegroundColor = originalColor;
        }

        public void Log(LogLevel level, string source, string message, Exception exception)
        {
            if (level < _minLevel)
                return; // Skip logging below minimum level

            var originalColor = Console.ForegroundColor;
            Console.ForegroundColor = GetColor(level);
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
            Console.ForegroundColor = colorOverride ?? GetColor(level);
            Console.WriteLine($"[{level}] [{source}] {message}");
            Console.ForegroundColor = originalColor;
        }


        private ConsoleColor GetColor(LogLevel level)
        {
            return level switch
            {
                LogLevel.Trace => ConsoleColor.Gray,
                LogLevel.Debug => ConsoleColor.Cyan,
                LogLevel.Information => ConsoleColor.Green,
                LogLevel.Warning => ConsoleColor.Yellow,
                LogLevel.Error => ConsoleColor.Red,
                LogLevel.Critical => ConsoleColor.Magenta,
                _ => ConsoleColor.White,
            };
        }
    }
}
