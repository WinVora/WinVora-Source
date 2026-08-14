using System;
using System.Diagnostics;

namespace WinVora
{
    internal static class UpdateApplicationShutdownService
    {
        public static bool TryCloseForUpdate(string packageId, string packageName)
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
                return false;
            }

            try
            {
                foreach (string processName in processNames)
                {
                    foreach (var process in Process.GetProcessesByName(processName))
                    {
                        using (process)
                        {
                            if (process.HasExited) continue;
                            Logger.Log($"Schließe {process.ProcessName} ({process.Id}) für Programm-Update.");
                            process.Kill(entireProcessTree: true);
                            process.WaitForExit(5000);
                        }
                    }
                }

                return processNames.All(name => Process.GetProcessesByName(name).Length == 0);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Programm für Update schließen ({packageId})", ex);
                return false;
            }
        }
    }
}
