using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace WinVora
{
    internal static class LegacyFeatureCleanup
    {
        private static readonly string MarkerPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinVora", ".legacy-maintenance-task-cleaned-v1");

        public static async Task RemoveMaintenanceTasksOnceAsync()
        {
            if (File.Exists(MarkerPath)) return;

            foreach (string taskName in new[] { "WinVora Maintenance", "WinVoraMaintenance" })
                await DeleteScheduledTaskAsync(taskName);

            Directory.CreateDirectory(Path.GetDirectoryName(MarkerPath)!);
            await File.WriteAllTextAsync(MarkerPath, DateTime.UtcNow.ToString("O"));
            Logger.Log("Veraltete WinVora-Wartungsaufgaben wurden geprüft und bereinigt.");
        }

        private static async Task DeleteScheduledTaskAsync(string taskName)
        {
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "schtasks.exe",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };
                process.StartInfo.ArgumentList.Add("/Delete");
                process.StartInfo.ArgumentList.Add("/TN");
                process.StartInfo.ArgumentList.Add(taskName);
                process.StartInfo.ArgumentList.Add("/F");
                process.Start();
                await process.WaitForExitAsync();
                if (process.ExitCode == 0)
                    Logger.Log($"Veraltete Aufgabenplanung '{taskName}' entfernt.");
            }
            catch (Exception ex)
            {
                Logger.LogErrorOnce($"Veraltete Aufgabenplanung '{taskName}' bereinigen", ex);
            }
        }
    }
}
