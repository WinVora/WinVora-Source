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

        // Verhindert, dass die Logdatei über die Zeit unbegrenzt wächst.
        private static void TrimIfTooLarge()
        {
            const long maxBytes = 2 * 1024 * 1024; // 2 MB

            var info = new FileInfo(LogFilePath);
            if (!info.Exists || info.Length <= maxBytes) return;

            var lines = File.ReadAllLines(LogFilePath);
            var keep = lines.Length > 2000 ? lines[^2000..] : lines;
            File.WriteAllLines(LogFilePath, keep);
        }
    }
}