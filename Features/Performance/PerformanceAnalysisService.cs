using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WinVora
{
    internal enum PerformanceFindingSeverity { Info, Warning, Critical }

    internal sealed record PerformanceFinding(
        string Title,
        string Description,
        PerformanceFindingSeverity Severity,
        string ActionText,
        string TargetPage,
        string Glyph);

    internal sealed record PerformanceAnalysisResult(
        IReadOnlyList<PerformanceFinding> Findings,
        IReadOnlyList<string> PassedChecks,
        int ChecksCompleted,
        DateTime CheckedAt);

    internal static class PerformanceAnalysisService
    {
        private static readonly SemaphoreSlim AnalysisGate = new(1, 1);

        public static async Task<PerformanceAnalysisResult> AnalyzeAsync(
            int availableUpdates,
            SecurityHealthState securityState,
            SystemInfoSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            await AnalysisGate.WaitAsync(cancellationToken);
            try
            {
                return await Task.Run(() => Analyze(
                    availableUpdates, securityState, snapshot, cancellationToken), cancellationToken);
            }
            finally
            {
                AnalysisGate.Release();
            }
        }

        private static PerformanceAnalysisResult Analyze(
            int availableUpdates,
            SecurityHealthState securityState,
            SystemInfoSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            var findings = new List<PerformanceFinding>();
            var passed = new List<string>();
            int checks = 0;
            cancellationToken.ThrowIfCancellationRequested();
            var telemetry = HardwareTelemetryService.GetSnapshot(refreshSensors: true, cancellationToken);

            checks++;
            if (RestartDetectionService.IsExplicitWindowsRestartPending())
                findings.Add(Finding("Performance.RestartTitle", "Performance.RestartDetail",
                    PerformanceFindingSeverity.Warning, "Performance.OpenRestart", "Restart", "\uE777"));
            else passed.Add(Localization.T("Performance.PassRestart"));

            AnalyzeDrives(findings, passed, ref checks, cancellationToken);
            AnalyzeStartup(findings, passed, ref checks, cancellationToken);
            AnalyzeUsage(findings, passed, ref checks, telemetry, cancellationToken);
            AnalyzeHardware(findings, passed, ref checks, telemetry.Sensors, cancellationToken);
            AnalyzeWindows(findings, passed, ref checks, snapshot, cancellationToken);
            AnalyzeBattery(findings, passed, ref checks, cancellationToken);
            var extended = ExtendedPcCheckService.Analyze(cancellationToken, telemetry);
            findings.AddRange(extended.Findings);
            passed.AddRange(extended.PassedChecks);
            checks += extended.ChecksCompleted;

            checks++;
            if (availableUpdates > 0)
                findings.Add(new PerformanceFinding(
                    Localization.T("Performance.UpdatesTitle"),
                    Localization.F("Performance.UpdatesDetail", availableUpdates),
                    PerformanceFindingSeverity.Info,
                    Localization.T("Performance.OpenUpdates"), "Updates", "\uE895"));
            else passed.Add(Localization.T("Performance.PassUpdates"));

            checks++;
            if (securityState == SecurityHealthState.Problem)
                findings.Add(Finding("Performance.SecurityTitle", "Performance.SecurityDetail",
                    PerformanceFindingSeverity.Critical, "Performance.OpenWindowsSecurity", "WindowsSecurity", "\uEA18"));
            else if (securityState == SecurityHealthState.Active)
                passed.Add(Localization.T("Performance.PassSecurity"));
            else
                findings.Add(Finding("Performance.SecurityUnknownTitle", "Performance.SecurityUnknownDetail",
                    PerformanceFindingSeverity.Info, "Performance.OpenWindowsSecurity", "WindowsSecurity", "\uEA18"));

            return new PerformanceAnalysisResult(findings, passed, checks, DateTime.Now);
        }

        private static void AnalyzeDrives(List<PerformanceFinding> findings, List<string> passed,
            ref int checks, CancellationToken cancellationToken)
        {
            bool problem = false;
            foreach (var drive in DriveInfo.GetDrives())
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (!drive.IsReady || drive.DriveType != DriveType.Fixed) continue;
                    checks++;
                    double freePercent = drive.TotalSize <= 0 ? 100 : drive.AvailableFreeSpace * 100d / drive.TotalSize;
                    if (freePercent >= 12 && drive.AvailableFreeSpace >= 15L * 1024 * 1024 * 1024) continue;
                    problem = true;
                    findings.Add(new PerformanceFinding(
                        Localization.F("Performance.LowDiskTitle", drive.Name),
                        Localization.F("Performance.LowDiskDetail", StorageService.FormatBytes(drive.AvailableFreeSpace),
                            StorageService.FormatBytes(drive.TotalSize), freePercent),
                        freePercent < 6 ? PerformanceFindingSeverity.Critical : PerformanceFindingSeverity.Warning,
                        Localization.T("Performance.OpenStorage"), "Storage", "\uEDA2"));
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Laufwerk für PC-Check prüfen ({drive.Name})", ex);
                }
            }
            if (!problem) passed.Add(Localization.T("Performance.PassDisk"));
        }

        private static void AnalyzeStartup(List<PerformanceFinding> findings, List<string> passed,
            ref int checks, CancellationToken cancellationToken)
        {
            var entries = AutostartService.GetEntries();
            cancellationToken.ThrowIfCancellationRequested();
            int enabled = entries.Count(entry => entry.Enabled);
            int missing = entries.Count(entry => !AutostartService.CommandTargetExists(entry.Command));
            checks += 2;
            if (enabled > 8)
                findings.Add(new PerformanceFinding(Localization.T("Performance.StartupTitle"),
                    Localization.F("Performance.StartupDetail", enabled), PerformanceFindingSeverity.Warning,
                    Localization.T("Performance.OpenStartup"), "Autostart", "\uE768"));
            else passed.Add(Localization.T("Performance.PassStartupCount"));
            if (missing > 0)
                findings.Add(new PerformanceFinding(Localization.T("Performance.MissingStartupTitle"),
                    Localization.F("Performance.MissingStartupDetail", missing), PerformanceFindingSeverity.Warning,
                    Localization.T("Performance.OpenStartup"), "Autostart", "\uE7BA"));
            else passed.Add(Localization.T("Performance.PassStartupFiles"));
        }

        private static void AnalyzeUsage(List<PerformanceFinding> findings, List<string> passed,
            ref int checks, HardwareTelemetrySnapshot telemetry, CancellationToken cancellationToken)
        {
            double cpu = telemetry.CpuPercent;
            double ram = telemetry.RamPercent;
            cancellationToken.ThrowIfCancellationRequested();
            checks += 2;
            if (cpu >= 85)
                findings.Add(new PerformanceFinding(Localization.T("Performance.HighCpuTitle"),
                    Localization.F("Performance.HighCpuDetail", cpu), PerformanceFindingSeverity.Warning,
                    Localization.T("Performance.OpenTaskManager"), "TaskManager", "\uE950"));
            else passed.Add(Localization.T("Performance.PassCpu"));
            if (ram >= 85)
                findings.Add(new PerformanceFinding(Localization.T("Performance.HighRamTitle"),
                    Localization.F("Performance.HighRamDetail", ram), PerformanceFindingSeverity.Critical,
                    Localization.T("Performance.OpenTaskManager"), "TaskManager", "\uE950"));
            else passed.Add(Localization.T("Performance.PassRam"));

            string largestName = string.Empty;
            long largestBytes = 0;
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        long bytes = process.WorkingSet64;
                        if (bytes <= largestBytes) continue;
                        largestBytes = bytes;
                        largestName = process.ProcessName;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Prozessspeicher nicht lesbar ({process.Id}): {ex.Message}");
                    }
                }
            }
            checks++;
            if (largestBytes >= 1536L * 1024 * 1024)
                findings.Add(new PerformanceFinding(Localization.F("Performance.ProcessTitle", largestName),
                    Localization.F("Performance.ProcessDetail", largestBytes / 1024d / 1024 / 1024),
                    PerformanceFindingSeverity.Warning, Localization.T("Performance.OpenTaskManager"), "TaskManager", "\uE9D9"));
            else passed.Add(Localization.T("Performance.PassProcesses"));
        }

        private static void AnalyzeHardware(List<PerformanceFinding> findings, List<string> passed,
            ref int checks, HardwareReadings readings, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (readings.CpuTemperature is double cpuTemp || readings.GpuTemperature is double)
            {
                checks++;
                double hottest = Math.Max(readings.CpuTemperature ?? 0, readings.GpuTemperature ?? 0);
                if (hottest >= 90)
                    findings.Add(new PerformanceFinding(Localization.T("Performance.HighTemperatureTitle"),
                        Localization.F("Performance.HighTemperatureDetail", hottest), PerformanceFindingSeverity.Critical,
                        Localization.T("Performance.OpenHardwareDetails"), "System", "\uE7E7"));
                else passed.Add(Localization.T("Performance.PassTemperature"));
            }
            if (readings.GpuLoadPercent is double gpu)
            {
                checks++;
                if (gpu >= 95)
                    findings.Add(new PerformanceFinding(Localization.T("Performance.HighGpuTitle"),
                        Localization.F("Performance.HighGpuDetail", gpu), PerformanceFindingSeverity.Warning,
                        Localization.T("Performance.OpenTaskManager"), "TaskManager", "\uE7F8"));
                else passed.Add(Localization.T("Performance.PassGpu"));
            }
        }

        private static void AnalyzeWindows(List<PerformanceFinding> findings, List<string> passed,
            ref int checks, SystemInfoSnapshot snapshot, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(snapshot.ActivationStatus))
            {
                checks++;
                if (ContainsNegative(snapshot.ActivationStatus))
                    findings.Add(Finding("Performance.ActivationTitle", "Performance.ActivationDetail",
                        PerformanceFindingSeverity.Warning, "Performance.OpenActivation", "ActivationSettings", "\uE73E"));
                else passed.Add(Localization.T("Performance.PassActivation"));
            }
            if (!string.IsNullOrWhiteSpace(snapshot.SecureBoot) && !ContainsUnavailable(snapshot.SecureBoot))
            {
                checks++;
                if (ContainsNegative(snapshot.SecureBoot))
                    findings.Add(Finding("Performance.SecureBootTitle", "Performance.SecureBootDetail",
                        PerformanceFindingSeverity.Info, "Performance.OpenWindowsSystemInfo", "WindowsSystemInfo", "\uEA18"));
                else passed.Add(Localization.T("Performance.PassSecureBoot"));
            }
            if (!string.IsNullOrWhiteSpace(snapshot.TpmVersion) && !ContainsUnavailable(snapshot.TpmVersion))
            {
                checks++;
                passed.Add(Localization.T("Performance.PassTpm"));
            }
            if (DateTime.TryParse(snapshot.LastUpdate, out DateTime lastUpdate))
            {
                checks++;
                int days = Math.Max(0, (DateTime.Today - lastUpdate.Date).Days);
                if (days > 45)
                    findings.Add(new PerformanceFinding(Localization.T("Performance.OldUpdateTitle"),
                        Localization.F("Performance.OldUpdateDetail", days), PerformanceFindingSeverity.Warning,
                        Localization.T("Performance.OpenWindowsUpdate"), "WindowsUpdate", "\uE895"));
                else passed.Add(Localization.T("Performance.PassWindowsUpdate"));
            }
            if (TryReadUptimeDays(snapshot.Uptime, out int uptimeDays))
            {
                checks++;
                if (uptimeDays >= 14)
                    findings.Add(new PerformanceFinding(Localization.T("Performance.LongUptimeTitle"),
                        Localization.F("Performance.LongUptimeDetail", uptimeDays), PerformanceFindingSeverity.Info,
                        Localization.T("Performance.OpenRestart"), "Restart", "\uE777"));
                else passed.Add(Localization.T("Performance.PassUptime"));
            }
        }

        private static void AnalyzeBattery(List<PerformanceFinding> findings, List<string> passed,
            ref int checks, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var battery = UpdatePowerGuard.ReadBatteryState();
            if (!battery.HasBattery) return;
            checks++;
            if (battery.ChargePercent <= 15 && !battery.Charging)
                findings.Add(new PerformanceFinding(Localization.T("Performance.LowBatteryTitle"),
                    Localization.F("Performance.LowBatteryDetail", battery.ChargePercent), PerformanceFindingSeverity.Warning,
                    Localization.T("Performance.OpenBatterySettings"), "BatterySettings", "\uE850"));
            else passed.Add(Localization.T("Performance.PassBattery"));
        }

        private static bool ContainsNegative(string value) =>
            value.Contains("deaktiv", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("disabled", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("nicht aktiviert", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("not activated", StringComparison.OrdinalIgnoreCase);

        private static bool ContainsUnavailable(string value) =>
            value.Contains("nicht verfügbar", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("not available", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("unbekannt", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("unknown", StringComparison.OrdinalIgnoreCase);

        private static bool TryReadUptimeDays(string value, out int days)
        {
            days = 0;
            if (string.IsNullOrWhiteSpace(value)) return false;
            string first = value.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            return first.EndsWith('d') && int.TryParse(first[..^1], out days);
        }

        private static PerformanceFinding Finding(string titleKey, string descriptionKey,
            PerformanceFindingSeverity severity, string actionKey, string targetPage, string glyph) =>
            new(Localization.T(titleKey), Localization.T(descriptionKey), severity,
                Localization.T(actionKey), targetPage, glyph);
    }
}
