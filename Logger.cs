using System;
using System.IO;

namespace WinVora
{
    public static class Logger
    {
        private static readonly object _lock = new();

        private static string LogFilePath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WinVora",
                "log.txt");

        public static string GetLogFilePath() => LogFilePath;

        public static void Clear()
        {
            try
            {
                if (File.Exists(LogFilePath))
                    File.WriteAllText(LogFilePath, string.Empty);
            }
            catch
            {
                // Nicht kritisch
            }
        }

        public static void Log(string message)
        {
            try
            {
                lock (_lock)
                {
                    var dir = Path.GetDirectoryName(LogFilePath);
                    if (dir != null) Directory.CreateDirectory(dir);

                    var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
                    File.AppendAllText(LogFilePath, line);

                    TrimIfTooLarge();
                }
            }
            catch
            {
                // Logging darf die App niemals selbst zum Absturz bringen
            }
        }

        public static void LogError(string context, Exception ex)
        {
            Log($"FEHLER in {context}: {ex.GetType().Name}: {ex.Message}");
        }

        // Rotiert die Logdatei, statt sie unbegrenzt wachsen zu lassen. Bis zu
        // fünf ältere Dateien bleiben für Diagnosen erhalten.
        private static void TrimIfTooLarge()
        {
            const long maxBytes = 2 * 1024 * 1024; // 2 MB

            var info = new FileInfo(LogFilePath);
            if (!info.Exists || info.Length <= maxBytes) return;

            const int retainedLogs = 5;
            string oldest = LogFilePath + $".{retainedLogs}";
            if (File.Exists(oldest)) File.Delete(oldest);
            for (int index = retainedLogs - 1; index >= 1; index--)
            {
                string source = LogFilePath + $".{index}";
                string target = LogFilePath + $".{index + 1}";
                if (File.Exists(source)) File.Move(source, target, overwrite: true);
            }
            File.Move(LogFilePath, LogFilePath + ".1", overwrite: true);
            File.WriteAllText(LogFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Logdatei rotiert.{Environment.NewLine}");
        }
    }
}
