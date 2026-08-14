using System;
using System.IO;
using System.Linq;

namespace WinVora
{
    internal sealed record WingetFailureDiagnostic(
        bool RequiresElevation,
        bool RequiresApplicationShutdown,
        int? HiddenExitCode,
        string Details);

    internal static class WingetFailureDiagnostics
    {
        public static WingetFailureDiagnostic Analyze(string packageId, DateTime startedUtc)
        {
            try
            {
                string root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Packages",
                    "Microsoft.DesktopAppInstaller_8wekyb3d8bbwe",
                    "LocalState",
                    "DiagOutputDir");
                if (!Directory.Exists(root)) return Empty();

                var files = Directory.EnumerateFiles(root, "*.log")
                    .Select(path => new FileInfo(path))
                    .Where(file => file.LastWriteTimeUtc >= startedUtc.AddSeconds(-2))
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .Take(12);
                string combined = string.Join("\n", files.Select(file => SafeRead(file.FullName)));
                if (string.IsNullOrWhiteSpace(combined)) return Empty();

                var parsed = AnalyzeText(combined);
                string details = string.Join("\n", combined.Split('\n')
                    .Where(line => line.Contains(packageId, StringComparison.OrdinalIgnoreCase) ||
                                   line.Contains("0x80073D02", StringComparison.OrdinalIgnoreCase) ||
                                   line.Contains("0x80070005", StringComparison.OrdinalIgnoreCase) ||
                                   line.Contains("Error: -2147024891", StringComparison.OrdinalIgnoreCase) ||
                                   line.Contains("ShellExecute installer failed", StringComparison.OrdinalIgnoreCase))
                    .Take(12));
                return parsed with { Details = details };
            }
            catch (Exception ex)
            {
                Logger.LogErrorOnce($"WinGet-Diagnose auswerten ({packageId})", ex);
                return Empty();
            }
        }

        internal static WingetFailureDiagnostic AnalyzeText(string text)
        {
            bool appInUse = text.Contains("0x80073D02", StringComparison.OrdinalIgnoreCase) ||
                            text.Contains("Apps geschlossen werden müssen", StringComparison.OrdinalIgnoreCase) ||
                            text.Contains("apps need to be closed", StringComparison.OrdinalIgnoreCase);
            bool accessDenied = text.Contains("0x80070005", StringComparison.OrdinalIgnoreCase) ||
                                text.Contains("Error: -2147024891", StringComparison.OrdinalIgnoreCase) ||
                                text.Contains("Zugriff verweigert", StringComparison.OrdinalIgnoreCase) ||
                                text.Contains("access is denied", StringComparison.OrdinalIgnoreCase);
            int? hiddenCode = appInUse
                ? unchecked((int)0x80073D02)
                : accessDenied ? unchecked((int)0x80070005) : null;
            return new WingetFailureDiagnostic(accessDenied, appInUse, hiddenCode, string.Empty);
        }

        private static string SafeRead(string path)
        {
            try
            {
                const int maxCharacters = 96 * 1024;
                string text = File.ReadAllText(path);
                return text.Length <= maxCharacters ? text : text[^maxCharacters..];
            }
            catch (IOException) { return string.Empty; }
            catch (UnauthorizedAccessException) { return string.Empty; }
        }

        private static WingetFailureDiagnostic Empty() => new(false, false, null, string.Empty);
    }
}
