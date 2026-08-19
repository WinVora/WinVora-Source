using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Diagnostics;

namespace WinVora
{
    public static class Logger
    {
        private static readonly object _lock = new();
        private static readonly ConcurrentDictionary<string, byte> _reportedOnce = new();

        private static string LogFilePath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WinVora",
                "log.txt");

        public static string GetLogFilePath() => LogFilePath;

        public static string ReadForDiagnostics(int archivedFiles = 2)
        {
            var content = new StringBuilder();
            try
            {
                var paths = new List<string>();
                for (int index = Math.Max(0, archivedFiles); index >= 1; index--)
                    paths.Add(LogFilePath + $".{index}");
                paths.Add(LogFilePath);

                foreach (string path in paths)
                {
                    if (!File.Exists(path)) continue;
                    content.AppendLine($"--- {Path.GetFileName(path)} ---");
                    content.AppendLine(File.ReadAllText(path));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WinVora-Protokolle konnten nicht gelesen werden: {ex}");
            }
            return content.Length == 0 ? "Kein Protokoll vorhanden." : content.ToString();
        }

        public static void Clear()
        {
            try
            {
                if (File.Exists(LogFilePath))
                    File.WriteAllText(LogFilePath, string.Empty);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WinVora-Protokoll konnte nicht geleert werden: {ex}");
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
            catch (Exception ex)
            {
                Debug.WriteLine($"WinVora-Protokoll konnte nicht geschrieben werden: {ex}");
            }
        }

        public static void LogError(string context, Exception ex)
        {
            if (ex == null)
            {
                Log($"FEHLER in {context}: Keine Exception-Information verfügbar.");
                return;
            }

            var details = new StringBuilder()
                .Append("FEHLER in ").AppendLine(context)
                .Append("Thread: ").AppendLine(Environment.CurrentManagedThreadId.ToString())
                .Append("HRESULT: 0x").AppendLine(ex.HResult.ToString("X8"))
                .AppendLine("Exception:")
                .Append(ex);

            Log(details.ToString());
        }

        public static void LogErrorOnce(string context, Exception ex)
        {
            string key = $"{context}|{ex.GetType().FullName}|{ex.HResult:X8}";
            if (_reportedOnce.TryAdd(key, 0)) LogError(context, ex);
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
