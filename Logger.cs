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

        // Rotiert die Logdatei, statt bei jedem Grenzwert die komplette Datei
        // einzulesen und neu zu schreiben. Eine vorherige Sitzung bleibt erhalten.
        private static void TrimIfTooLarge()
        {
            const long maxBytes = 2 * 1024 * 1024; // 2 MB

            var info = new FileInfo(LogFilePath);
            if (!info.Exists || info.Length <= maxBytes) return;

            string previousPath = LogFilePath + ".1";
            if (File.Exists(previousPath)) File.Delete(previousPath);
            File.Move(LogFilePath, previousPath);
            File.WriteAllText(LogFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Logdatei rotiert.{Environment.NewLine}");
        }
    }
}
