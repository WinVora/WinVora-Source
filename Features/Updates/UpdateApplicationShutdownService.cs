using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace WinVora
{
    internal enum ApplicationShutdownState
    {
        Closed,
        RequiresForceClose,
        Failed
    }

    internal static class UpdateApplicationShutdownService
    {
        private static readonly TimeSpan GracefulCloseTimeout = TimeSpan.FromSeconds(8);

        public static async Task<ApplicationShutdownState> TryCloseGracefullyForUpdateAsync(
            string packageId,
            string packageName,
            CancellationToken cancellationToken)
        {
            // Prozesse werden nur über eine geprüfte Zuordnung beendet. Aus einem
            // Anzeigenamen wie "Microsoft ..." einen Prozessnamen abzuleiten wäre
            // zu ungenau und könnte ein unbeteiligtes Programm treffen.
            string[] processNames = packageId.Equals("Anthropic.Claude", StringComparison.OrdinalIgnoreCase)
                ? new[] { "Claude" }
                : Array.Empty<string>();

            if (processNames.Length == 0)
            {
                Logger.Log($"Keine sichere Prozesszuordnung für Update von {packageName} [{packageId}].");
                return ApplicationShutdownState.Failed;
            }

            try
            {
                var running = GetRunningProcesses(processNames);
                try
                {
                    if (running.Count == 0)
                        return ApplicationShutdownState.Closed;

                    bool closeRequested = false;
                    foreach (Process process in running)
                    {
                        if (process.MainWindowHandle == 0) continue;
                        Logger.Log($"Bitte {process.ProcessName} ({process.Id}) um geordnetes Beenden für Programm-Update.");
                        closeRequested |= process.CloseMainWindow();
                    }

                    if (closeRequested && await WaitUntilClosedAsync(running, cancellationToken).ConfigureAwait(false))
                        return ApplicationShutdownState.Closed;

                    Logger.Log($"{packageName} reagierte nicht rechtzeitig auf die normale Schließanforderung.");
                    return ApplicationShutdownState.RequiresForceClose;
                }
                finally
                {
                    foreach (Process process in running) process.Dispose();
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Logger.LogError($"Programm geordnet für Update schließen ({packageId})", ex);
                return ApplicationShutdownState.Failed;
            }
        }

        public static bool ForceCloseForUpdate(string packageId, string packageName)
        {
            string[] processNames = packageId.Equals("Anthropic.Claude", StringComparison.OrdinalIgnoreCase)
                ? new[] { "Claude" }
                : Array.Empty<string>();
            if (processNames.Length == 0) return false;

            try
            {
                foreach (string processName in processNames)
                {
                    foreach (var process in Process.GetProcessesByName(processName))
                    {
                        using (process)
                        {
                            if (process.HasExited) continue;
                            Logger.Log($"Beende {process.ProcessName} ({process.Id}) nach ausdrücklicher Bestätigung für Programm-Update.");
                            process.Kill(entireProcessTree: true);
                            process.WaitForExit(5000);
                        }
                    }
                }

                var remaining = GetRunningProcesses(processNames);
                bool allClosed = remaining.Count == 0;
                foreach (Process process in remaining) process.Dispose();
                return allClosed;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Programm für Update schließen ({packageId})", ex);
                return false;
            }
        }

        private static List<Process> GetRunningProcesses(IEnumerable<string> processNames)
        {
            var result = new List<Process>();
            foreach (string name in processNames)
            {
                foreach (Process process in Process.GetProcessesByName(name))
                {
                    try
                    {
                        if (!process.HasExited) result.Add(process);
                        else process.Dispose();
                    }
                    catch
                    {
                        process.Dispose();
                    }
                }
            }
            return result;
        }

        private static async Task<bool> WaitUntilClosedAsync(
            IReadOnlyCollection<Process> processes,
            CancellationToken cancellationToken)
        {
            var deadline = Stopwatch.StartNew();
            while (deadline.Elapsed < GracefulCloseTimeout)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool allExited = processes.All(process =>
                {
                    try { return process.HasExited; }
                    catch { return true; }
                });
                if (allExited) return true;
                await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            }
            return false;
        }
    }
}
