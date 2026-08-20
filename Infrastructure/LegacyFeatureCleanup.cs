using System;
using System.IO;
using System.Threading;
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
            {
                if (!await DeleteScheduledTaskAsync(taskName).ConfigureAwait(false))
                {
                    Logger.Log($"Legacy-Cleanup wird später erneut versucht: {taskName}");
                    return;
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(MarkerPath)!);
            await File.WriteAllTextAsync(MarkerPath, DateTime.UtcNow.ToString("O"));
            Logger.Log("Veraltete WinVora-Wartungsaufgaben wurden geprüft und bereinigt.");
        }

        private static async Task<bool> DeleteScheduledTaskAsync(string taskName)
        {
            try
            {
                string scheduler = Path.Combine(Environment.SystemDirectory, "schtasks.exe");
                var queryInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = scheduler
                };
                queryInfo.ArgumentList.Add("/Query");
                queryInfo.ArgumentList.Add("/TN");
                queryInfo.ArgumentList.Add(taskName);
                ProcessRunResult query = await SystemAccess.ProcessRunner.RunAsync(
                    queryInfo,
                    TimeSpan.FromSeconds(10),
                    CancellationToken.None).ConfigureAwait(false);
                if (query.TimedOut) return false;
                if (query.ExitCode != 0)
                    return true; // Aufgabe existiert nicht mehr.

                var deleteInfo = new System.Diagnostics.ProcessStartInfo { FileName = scheduler };
                deleteInfo.ArgumentList.Add("/Delete");
                deleteInfo.ArgumentList.Add("/TN");
                deleteInfo.ArgumentList.Add(taskName);
                deleteInfo.ArgumentList.Add("/F");
                ProcessRunResult deletion = await SystemAccess.ProcessRunner.RunAsync(
                    deleteInfo,
                    TimeSpan.FromSeconds(15),
                    CancellationToken.None).ConfigureAwait(false);
                if (!deletion.TimedOut && deletion.ExitCode == 0)
                {
                    Logger.Log($"Veraltete Aufgabenplanung '{taskName}' entfernt.");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Logger.LogErrorOnce($"Veraltete Aufgabenplanung '{taskName}' bereinigen", ex);
                return false;
            }
        }
    }
}
