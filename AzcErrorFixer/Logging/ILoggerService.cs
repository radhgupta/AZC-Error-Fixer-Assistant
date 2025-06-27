namespace AzcAnalyzerFixer.Logging
{
    public interface ILoggerService
    {
        void LogInfo(string message);
        void LogError(string message, Exception? ex = null);
    }

    public class ConsoleLoggerService : ILoggerService
    {
        public void LogInfo(string message)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(message);
            Console.ResetColor();
        }

        public void LogError(string message, Exception? ex = null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"ERROR: {message}");
            if (ex != null)
            {
                Console.WriteLine($"Exception: {ex.Message}");
            }
            Console.ResetColor();
        }
    }
}
