using System;
using System.Diagnostics;
using System.Collections.Generic;

namespace WinVora
{
    internal static class CoreLogicSelfTests
    {
        [Conditional("DEBUG")]
        public static void Run()
        {
            var package = WingetTableParser.Parse(
                "Demo App            Vendor.Demo       1.0       2.0       winget",
                new[] { 0, 20, 38, 48, 58 });
            Debug.Assert(package?.Id == "Vendor.Demo");
            Debug.Assert(package?.Available == "2.0");

            Debug.Assert(WingetErrorTranslator.ContainsRestartRequired("Restart required"));
            Debug.Assert(!WingetErrorTranslator.ContainsRestartRequired("Installation complete"));

            var settings = new AppSettings
            {
                StartupPage = "invalid",
                LiveUpdateIntervalSeconds = 99,
                Language = "xx",
                AnimationMode = "invalid"
            };
            settings.Validate();
            Debug.Assert(settings.StartupPage == "Übersicht");
            Debug.Assert(settings.LiveUpdateIntervalSeconds == 2);
            Debug.Assert(settings.Language == "de");
            Debug.Assert(settings.AnimationMode is "Full" or "Reduced" or "Off");
            Debug.Assert(StorageService.FormatBytes(0) == "0 B");
            Debug.Assert(StorageService.FormatBytes(1024).Contains("KB", StringComparison.Ordinal));
            Debug.Assert(StorageService.FormatBytes(1024 * 1024).Contains("MB", StringComparison.Ordinal));

            settings.IgnoredUpdateIds = new List<string> { "Vendor.App", "vendor.app", "" };
            settings.Validate();
            Debug.Assert(settings.IgnoredUpdateIds.Count == 1);

            Debug.Assert(SecurityStatusEvaluator.Evaluate("Aktiv", "Aktiv") == SecurityHealthState.Active);
            Debug.Assert(SecurityStatusEvaluator.Evaluate("Unbekannt", "Aktiv") == SecurityHealthState.Unknown);
            Debug.Assert(SecurityStatusEvaluator.Evaluate("Deaktiviert", "Aktiv") == SecurityHealthState.Problem);

            var csv = ProgramListExporter.ToCsv(new[]
            {
                new InstalledProgram { DisplayName = "Demo, App", Version = "1.0", Publisher = "Vendor" }
            }, english: true);
            Debug.Assert(csv.Contains("\"Demo, App\""));

            var diagnosticSnapshot = new SystemInfoSnapshot
            {
                ComputerName = "SECRET-PC", UserName = "SECRET-USER", SerialNumber = "SERIAL-123"
            };
            string sanitized = DiagnosticReportBuilder.Sanitize(
                "SECRET-PC SECRET-USER SERIAL-123 C:\\Users\\SECRET-USER\\file.txt 192.168.1.5 AA:BB:CC:DD:EE:FF",
                diagnosticSnapshot);
            Debug.Assert(!sanitized.Contains("SECRET-PC"));
            Debug.Assert(!sanitized.Contains("SERIAL-123"));
            Debug.Assert(!sanitized.Contains("192.168.1.5"));
            Debug.Assert(!sanitized.Contains("AA:BB:CC:DD:EE:FF"));

            Debug.Assert(InstalledProgramsService.TrySplitCommand(
                "\"C:\\Program Files (x86)\\Steam\\steam.exe\" steam://uninstall/123",
                out string steamExe, out string steamArgs));
            Debug.Assert(steamExe.EndsWith("steam.exe", StringComparison.OrdinalIgnoreCase));
            Debug.Assert(steamArgs == "steam://uninstall/123");
        }
    }
}
