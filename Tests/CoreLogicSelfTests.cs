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
            string shellExecuteFailure = WingetErrorTranslator.GetFriendlyMessage(
                unchecked((int)0x8A150006), "", WingetUpdateStatus.Failed);
            Debug.Assert(shellExecuteFailure.Contains("Installer", StringComparison.OrdinalIgnoreCase));
            Debug.Assert(!shellExecuteFailure.Contains("0x8A150006", StringComparison.OrdinalIgnoreCase));
            Debug.Assert(WingetErrorTranslator.RequiresElevation(unchecked((int)0x80073D28), ""));
            Debug.Assert(WingetErrorTranslator.RequiresElevation(unchecked((int)0x80070005), ""));
            Debug.Assert(!WingetErrorTranslator.RequiresElevation(0, "Installed successfully"));
            Debug.Assert(WingetElevationPolicy.RequiresElevationBeforeInstall("Anthropic.Claude"));
            Debug.Assert(WingetElevationPolicy.RequiresElevationBeforeInstall("anthropic.claude"));
            Debug.Assert(WingetElevationPolicy.RequiresElevationBeforeInstall("Microsoft.UpdateHealthTools"));
            Debug.Assert(!WingetElevationPolicy.RequiresElevationBeforeInstall("Vendor.Demo"));
            Debug.Assert(WingetElevationPolicy.RequiresApplicationShutdown("Anthropic.Claude"));
            Debug.Assert(!WingetElevationPolicy.RequiresApplicationShutdown("Vendor.Demo"));
            string appInUseMessage = WingetErrorTranslator.GetFriendlyMessage(
                unchecked((int)0x80073D02), "", WingetUpdateStatus.Failed);
            Debug.Assert(appInUseMessage.Contains("geschlossen", StringComparison.OrdinalIgnoreCase) ||
                         appInUseMessage.Contains("closed", StringComparison.OrdinalIgnoreCase));
            var appInUseDiagnostic = WingetFailureDiagnostics.AnalyzeText("Installer failed with 0x80073D02; apps need to be closed");
            Debug.Assert(appInUseDiagnostic.RequiresApplicationShutdown);
            Debug.Assert(appInUseDiagnostic.HiddenExitCode == unchecked((int)0x80073D02));
            var accessDiagnostic = WingetFailureDiagnostics.AnalyzeText("Error: -2147024891 (0x80070005)");
            Debug.Assert(accessDiagnostic.RequiresElevation);
            Debug.Assert(accessDiagnostic.HiddenExitCode == unchecked((int)0x80070005));

            var attemptedUpdate = new WingetPackage
            {
                Id = "Vendor.Demo",
                Version = "1.0",
                Available = "2.0"
            };
            Debug.Assert(WingetUpdateVerifier.IsStillUnchanged(attemptedUpdate, new[]
            {
                new WingetPackage { Id = "Vendor.Demo", Version = "1.0", Available = "2.0" }
            }));
            Debug.Assert(!WingetUpdateVerifier.IsStillUnchanged(attemptedUpdate, Array.Empty<WingetPackage>()));
            Debug.Assert(!WingetUpdateVerifier.IsStillUnchanged(attemptedUpdate, new[]
            {
                // Version 2.0 wurde installiert; falls bereits 3.0 angeboten
                // wird, war der vorherige Installationslauf trotzdem erfolgreich.
                new WingetPackage { Id = "Vendor.Demo", Version = "2.0", Available = "3.0" }
            }));

            var settings = new AppSettings
            {
                StartupPage = "invalid",
                LiveUpdateIntervalSeconds = 99,
                Language = "xx",
                AnimationMode = "invalid",
                UpdateChannel = "invalid"
            };
            settings.Validate();
            Debug.Assert(settings.StartupPage == "Übersicht");
            Debug.Assert(settings.LiveUpdateIntervalSeconds == 2);
            Debug.Assert(settings.Language == "de");
            Debug.Assert(settings.AnimationMode is "Full" or "Reduced" or "Off");
            Debug.Assert(settings.UpdateChannel == "Stable");
            Debug.Assert(UpdateService.IsNewerVersion("0.8.5-beta.1", "0.8.4.1"));
            Debug.Assert(!UpdateService.IsNewerVersion("0.8.4-beta.1", "0.8.4.1"));
            Debug.Assert(WingetTableParser.Parse("", Array.Empty<int>()) == null);
            Debug.Assert(!InstalledProgramsService.TrySplitCommand("", out _, out _));
            Debug.Assert(StorageService.FormatBytes(0) == "0 B");
            Debug.Assert(StorageService.FormatBytes(1024).Contains("KB", StringComparison.Ordinal));
            Debug.Assert(StorageService.FormatBytes(1024 * 1024).Contains("MB", StringComparison.Ordinal));

            settings.IgnoredUpdateIds = new List<string> { "Vendor.App", "vendor.app", "" };
            settings.Validate();
            Debug.Assert(settings.IgnoredUpdateIds.Count == 1);

            Debug.Assert(SecurityStatusEvaluator.Evaluate("Aktiv", "Aktiv") == SecurityHealthState.Active);
            Debug.Assert(SecurityStatusEvaluator.Evaluate("Unbekannt", "Aktiv") == SecurityHealthState.Unknown);
            Debug.Assert(SecurityStatusEvaluator.Evaluate("Deaktiviert", "Aktiv") == SecurityHealthState.Problem);
            Debug.Assert(SecurityStatusEvaluator.Evaluate("Nicht verfügbar", "Aktiv") == SecurityHealthState.Unknown);
            Debug.Assert(SecurityStatusEvaluator.Evaluate("Aktiv", "Nicht prüfbar") == SecurityHealthState.Unknown);
            Debug.Assert(SecurityStatusEvaluator.Evaluate("Teilweise/Inaktiv", "Aktiv") == SecurityHealthState.Problem);
            Debug.Assert(SecurityStatusEvaluator.Evaluate("Aktiv", "Deaktiviert") == SecurityHealthState.Problem);

            // Eine fehlgeschlagene WMI-Abfrage wird als unbekannt bewertet und
            // darf weder fälschlich Grün noch als echtes Problem Gelb ergeben.
            const string simulatedWmiFailure = "Unbekannt";
            Debug.Assert(SecurityStatusEvaluator.Evaluate(simulatedWmiFailure, "Aktiv") == SecurityHealthState.Unknown);

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

            var oldPcState = new PcStateSnapshot
            {
                CapturedUtc = DateTime.UtcNow.AddDays(-1),
                Programs = new Dictionary<string, string> { ["Demo|Vendor"] = "1.0", ["Removed|Vendor"] = "1.0" },
                StartupEntries = new HashSet<string> { "Old startup|old.exe" },
                DriveFreeBytes = new Dictionary<string, long> { ["C:\\"] = 1000 },
                WatchedFolderBytes = new Dictionary<string, long> { ["C:\\Users\\Demo\\Downloads"] = 100 }
            };
            var newPcState = new PcStateSnapshot
            {
                CapturedUtc = DateTime.UtcNow,
                Programs = new Dictionary<string, string> { ["Demo|Vendor"] = "2.0", ["New|Vendor"] = "1.0" },
                StartupEntries = new HashSet<string> { "New startup|new.exe" },
                DriveFreeBytes = new Dictionary<string, long> { ["C:\\"] = 500 },
                WatchedFolderBytes = new Dictionary<string, long> { ["C:\\Users\\Demo\\Downloads"] = 2L * 1024 * 1024 * 1024 }
            };
            var pcChanges = PcChangesService.Compare(oldPcState, newPcState);
            Debug.Assert(pcChanges.InstalledPrograms == 1 && pcChanges.RemovedPrograms == 1 && pcChanges.UpdatedPrograms == 1);
            Debug.Assert(pcChanges.AddedStartupEntries == 1 && pcChanges.RemovedStartupEntries == 1);
            Debug.Assert(pcChanges.StorageGrowth.Count == 1);
        }
    }
}
