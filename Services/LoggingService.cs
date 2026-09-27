using System;
using System.IO;

namespace InFox.Services
{
    public class LoggingService
    {
        private static readonly Lazy<LoggingService> _instance = new(() => new LoggingService());
        public static LoggingService Instance => _instance.Value;

        private readonly object _lock = new();
        private readonly string _logFilePath;

        private LoggingService()
        {
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string logDir = Path.Combine(localAppData, "inFaux", "logs");
                Directory.CreateDirectory(logDir);
                _logFilePath = Path.Combine(logDir, "infaux.log");

                // Truncate log if it exceeds 5 MB
                if (File.Exists(_logFilePath) && new FileInfo(_logFilePath).Length > 5 * 1024 * 1024)
                {
                    string oldLog = Path.Combine(logDir, "infaux.old.log");
                    File.Copy(_logFilePath, oldLog, true);
                    File.WriteAllText(_logFilePath, string.Empty);
                }
            }
            catch
            {
                _logFilePath = Path.Combine(Path.GetTempPath(), "infaux.log");
            }
        }

        public void Info(string category, string message) => Log("INFO", category, message);
        public void Warning(string category, string message) => Log("WARN", category, message);
        public void Error(string category, string message, Exception? ex = null)
        {
            string fullMessage = ex != null ? $"{message} | Exception: {ex.Message}\n{ex.StackTrace}" : message;
            Log("ERROR", category, fullMessage);
        }

        private void Log(string level, string category, string message)
        {
            string line = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] [{level}] [{category}] {message}";
            System.Diagnostics.Debug.WriteLine(line);

            try
            {
                lock (_lock)
                {
                    File.AppendAllText(_logFilePath, line + Environment.NewLine);
                }
            }
            catch
            {
                // Never throw from logger
            }
        }
    }
}
