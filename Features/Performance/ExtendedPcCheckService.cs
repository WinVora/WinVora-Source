using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Threading;

namespace WinVora
{
    internal sealed record ExtendedPcCheckResult(
        IReadOnlyList<PerformanceFinding> Findings,
        IReadOnlyList<string> PassedChecks,
        int ChecksCompleted);

    internal static class ExtendedPcCheckService
    {
        public static ExtendedPcCheckResult Analyze(CancellationToken token, HardwareTelemetrySnapshot telemetry)
        {
            var findings = new List<PerformanceFinding>();
            var passed = new List<string>();
            int checks = 0;
            Safe(() => AnalyzeHardware(findings, passed, ref checks, telemetry, token), "erweiterte Sensoren");
            Safe(() => AnalyzeNetwork(findings, passed, ref checks, token), "Netzwerk");
            Safe(() => AnalyzeBattery(findings, passed, ref checks, token), "Akkuverschleiß");
            Safe(() => AnalyzeDevices(findings, passed, ref checks, token), "Geräte");
            Safe(() => AnalyzeInstalledPrograms(findings, passed, ref checks, token), "installierte Programme");
            Safe(() => AnalyzeReliability(findings, passed, ref checks, token), "Zuverlässigkeit");
            Safe(() => AnalyzeServiceFailures(findings, passed, ref checks, token), "Windows-Dienste");
            Safe(() => AnalyzeHardwareEvents(findings, passed, ref checks, token), "WHEA");
            Safe(() => AnalyzeVirtualMemory(findings, passed, ref checks, token), "virtueller Speicher");
            return new ExtendedPcCheckResult(findings, passed, checks);
        }

        private static void AnalyzeHardware(List<PerformanceFinding> findings, List<string> passed,
            ref int checks, HardwareTelemetrySnapshot telemetry, CancellationToken token)
        {
            var readings = telemetry.Sensors;
            if (readings.Storage.Count > 0)
            {
                checks++;
                var hot = readings.Storage.Where(s => s.Temperature >= 70).ToList();
                var worn = readings.Storage.Where(s => s.RemainingLife is >= 0 and <= 10).ToList();
                if (hot.Count > 0)
                    findings.Add(Finding("Performance.StorageTempTitle", "Performance.StorageTempDetail",
                        PerformanceFindingSeverity.Critical, "Performance.OpenDiskSettings", "DiskSettings", "\uEDA2",
                        hot[0].Name, hot[0].Temperature ?? 0));
                else if (worn.Count > 0)
                    findings.Add(Finding("Performance.StorageLifeTitle", "Performance.StorageLifeDetail",
                        PerformanceFindingSeverity.Critical, "Performance.OpenDiskSettings", "DiskSettings", "\uEDA2",
                        worn[0].Name, worn[0].RemainingLife ?? 0));
                else passed.Add(Localization.T("Performance.PassStorageHealth"));
            }
            if (readings.Fans.Count > 0)
            {
                checks++;
                bool stoppedWhileHot = readings.Fans.Any(f => f.Rpm <= 0) &&
                    Math.Max(readings.CpuTemperature ?? 0, readings.GpuTemperature ?? 0) >= 80;
                if (stoppedWhileHot)
                    findings.Add(Simple("Performance.FanTitle", "Performance.FanDetail",
                        PerformanceFindingSeverity.Warning, "Performance.OpenHardwareDetails", "System", "\uE9CA"));
                else passed.Add(Localization.T("Performance.PassFans"));
            }
            if (readings.CpuClockMhz is double clock)
            {
                checks++;
                double cpu = telemetry.CpuPercent;
                if (cpu >= 70 && clock < 800)
                    findings.Add(Finding("Performance.LowClockTitle", "Performance.LowClockDetail",
                        PerformanceFindingSeverity.Warning, "Performance.OpenPowerOptions", "PowerOptions", "\uE950", clock, cpu));
                else passed.Add(Localization.T("Performance.PassClock"));
            }
            token.ThrowIfCancellationRequested();
        }

        private static void AnalyzeNetwork(List<PerformanceFinding> findings, List<string> passed,
            ref int checks, CancellationToken token)
        {
            var adapters = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback).ToList();
            if (adapters.Count == 0) return;
            checks++;
            long errors = 0;
            foreach (var adapter in adapters)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var stats = adapter.GetIPStatistics();
                    errors += stats.IncomingPacketsWithErrors + stats.OutgoingPacketsWithErrors;
                }
                catch (NetworkInformationException) { }
            }
            if (errors > 100)
                findings.Add(Finding("Performance.NetworkErrorsTitle", "Performance.NetworkErrorsDetail",
                    PerformanceFindingSeverity.Warning, "Performance.OpenNetworkSettings", "NetworkSettings", "\uE968", errors));
            else passed.Add(Localization.T("Performance.PassNetwork"));
        }

        internal static long? ReadNetworkErrorCount()
        {
            long errors = 0;
            bool found = false;
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces()
                         .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
            {
                try
                {
                    var stats = adapter.GetIPStatistics();
                    errors += stats.IncomingPacketsWithErrors + stats.OutgoingPacketsWithErrors;
                    found = true;
                }
                catch (NetworkInformationException) { }
            }
            return found ? errors : null;
        }

        private static void AnalyzeBattery(List<PerformanceFinding> findings, List<string> passed,
            ref int checks, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            double design = ReadFirstWmiNumber(@"root\WMI", "SELECT DesignedCapacity FROM BatteryStaticData", "DesignedCapacity");
            double full = ReadFirstWmiNumber(@"root\WMI", "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity", "FullChargedCapacity");
            if (design <= 0 || full <= 0) return;
            checks++;
            double health = Math.Clamp(full / design * 100, 0, 100);
            if (health < 60)
                findings.Add(Finding("Performance.BatteryWearTitle", "Performance.BatteryWearDetail",
                    PerformanceFindingSeverity.Warning, "Performance.OpenBatterySettings", "BatterySettings", "\uE850", health));
            else passed.Add(Localization.F("Performance.PassBatteryHealth", health));
        }

        internal static double? ReadBatteryHealthPercent()
        {
            try
            {
                double design = ReadFirstWmiNumber(@"root\WMI", "SELECT DesignedCapacity FROM BatteryStaticData", "DesignedCapacity");
                double full = ReadFirstWmiNumber(@"root\WMI", "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity", "FullChargedCapacity");
                return design > 0 && full > 0 ? Math.Clamp(full / design * 100, 0, 100) : null;
            }
            catch (Exception ex)
            {
                Logger.LogErrorOnce("Akkugesundheit lesen", ex);
                return null;
            }
        }

        private static void AnalyzeDevices(List<PerformanceFinding> findings, List<string> passed,
            ref int checks, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using var searcher = Search(@"root\CIMV2", "SELECT Name, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0 AND ConfigManagerErrorCode <> 22");
            var names = searcher.Get().Cast<ManagementObject>().Select(m => m["Name"]?.ToString()).Where(n => !string.IsNullOrWhiteSpace(n)).Take(5).ToList();
            checks++;
            if (names.Count > 0)
                findings.Add(Finding("Performance.DeviceErrorsTitle", "Performance.DeviceErrorsDetail",
                    PerformanceFindingSeverity.Warning, "Performance.OpenDeviceManager", "DeviceManager", "\uE7BA", names.Count, string.Join(", ", names)));
            else passed.Add(Localization.T("Performance.PassDevices"));
        }

        private static void AnalyzeReliability(List<PerformanceFinding> findings, List<string> passed,
            ref int checks, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string since = ManagementDateTimeConverter.ToDmtfDateTime(DateTime.Now.AddDays(-7));
            using var searcher = Search(@"root\CIMV2",
                $"SELECT SourceName FROM Win32_ReliabilityRecords WHERE TimeGenerated >= '{since}' " +
                "AND (SourceName='Application Error' OR SourceName='Windows Error Reporting')");
            int crashes = searcher.Get().Cast<ManagementObject>().Take(100).Count();
            checks++;
            if (crashes >= 5)
                findings.Add(Finding("Performance.ReliabilityTitle", "Performance.ReliabilityDetail",
                    PerformanceFindingSeverity.Warning, "Performance.OpenReliability", "ReliabilityMonitor", "\uE9D9", crashes));
            else passed.Add(Localization.T("Performance.PassReliability"));
        }

        private static void AnalyzeInstalledPrograms(List<PerformanceFinding> findings, List<string> passed,
            ref int checks, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var programs = InstalledProgramsService.GetInstalledPrograms(resolveIcons: false);
            var large = programs.Where(p => p.EstimatedSizeBytes >= 5L * 1024 * 1024 * 1024)
                .OrderByDescending(p => p.EstimatedSizeBytes).ToList();
            int missingLocations = programs.Count(p => !string.IsNullOrWhiteSpace(p.InstallLocation) &&
                                                        !System.IO.Directory.Exists(p.InstallLocation));
            checks += 2;
            if (large.Count > 0)
                findings.Add(Finding("Performance.LargeAppsTitle", "Performance.LargeAppsDetail",
                    PerformanceFindingSeverity.Info, "Performance.OpenUninstall", "Uninstall", "\uE74D",
                    large.Count, large[0].DisplayName, StorageService.FormatBytes(large[0].EstimatedSizeBytes)));
            else passed.Add(Localization.T("Performance.PassLargeApps"));
            if (missingLocations > 0)
                findings.Add(Finding("Performance.StaleAppsTitle", "Performance.StaleAppsDetail",
                    PerformanceFindingSeverity.Info, "Performance.OpenUninstall", "Uninstall", "\uE7BA", missingLocations));
            else passed.Add(Localization.T("Performance.PassAppEntries"));
        }

        private static void AnalyzeHardwareEvents(List<PerformanceFinding> findings, List<string> passed,
            ref int checks, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string since = ManagementDateTimeConverter.ToDmtfDateTime(DateTime.Now.AddDays(-7));
            using var searcher = Search(@"root\CIMV2", $"SELECT RecordNumber FROM Win32_NTLogEvent WHERE Logfile='System' AND SourceName='Microsoft-Windows-WHEA-Logger' AND TimeGenerated >= '{since}'");
            int count = searcher.Get().Cast<ManagementObject>().Take(100).Count();
            checks++;
            if (count > 0)
                findings.Add(Finding("Performance.WheaTitle", "Performance.WheaDetail",
                    PerformanceFindingSeverity.Critical, "Performance.OpenEventViewer", "EventViewerSystem", "\uEA39", count));
            else passed.Add(Localization.T("Performance.PassWhea"));
        }

        private static void AnalyzeServiceFailures(List<PerformanceFinding> findings, List<string> passed,
            ref int checks, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string since = ManagementDateTimeConverter.ToDmtfDateTime(DateTime.Now.AddDays(-7));
            using var searcher = Search(@"root\CIMV2",
                $"SELECT EventCode FROM Win32_NTLogEvent WHERE Logfile='System' " +
                $"AND SourceName='Service Control Manager' AND TimeGenerated >= '{since}' " +
                "AND (EventCode=7000 OR EventCode=7001 OR EventCode=7009 OR EventCode=7011 OR EventCode=7023 OR EventCode=7031)");
            int failures = searcher.Get().Cast<ManagementObject>().Take(100).Count();
            checks++;
            if (failures >= 3)
                findings.Add(Finding("Performance.ServiceFailuresTitle", "Performance.ServiceFailuresDetail",
                    PerformanceFindingSeverity.Warning, "Performance.OpenEventViewer", "EventViewerSystem", "\uE7BA", failures));
            else passed.Add(Localization.T("Performance.PassServices"));
        }

        private static void AnalyzeVirtualMemory(List<PerformanceFinding> findings, List<string> passed,
            ref int checks, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using var searcher = Search(@"root\CIMV2", "SELECT TotalVirtualMemorySize, FreeVirtualMemory FROM Win32_OperatingSystem");
            foreach (ManagementObject item in searcher.Get())
            {
                double total = Convert.ToDouble(item["TotalVirtualMemorySize"] ?? 0);
                double free = Convert.ToDouble(item["FreeVirtualMemory"] ?? 0);
                if (total <= 0) return;
                checks++;
                double used = (total - free) / total * 100;
                if (used >= 90)
                    findings.Add(Finding("Performance.VirtualMemoryTitle", "Performance.VirtualMemoryDetail",
                        PerformanceFindingSeverity.Warning, "Performance.OpenAdvancedSystem", "AdvancedSystemSettings", "\uE950", used));
                else passed.Add(Localization.T("Performance.PassVirtualMemory"));
                break;
            }
        }

        private static double ReadFirstWmiNumber(string scope, string query, string property)
        {
            try
            {
                foreach (var item in SystemAccess.Wmi.Query(scope, query, property))
                    return Convert.ToDouble(item.TryGetValue(property, out object? value) ? value ?? 0 : 0);
            }
            catch (ManagementException ex) when (
                ex.ErrorCode is ManagementStatus.InvalidClass or ManagementStatus.InvalidNamespace)
            {
                // Desktop-PCs besitzen die Akku-WMI-Klassen normalerweise
                // nicht. Das bedeutet "kein Akku", nicht "Diagnosefehler".
                return 0;
            }
            return 0;
        }

        private static ManagementObjectSearcher Search(string scope, string query)
        {
            var searcher = new ManagementObjectSearcher(scope, query);
            searcher.Options.Timeout = TimeSpan.FromSeconds(4);
            searcher.Options.ReturnImmediately = true;
            return searcher;
        }

        private static void Safe(Action action, string name)
        {
            try { action(); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { Logger.LogErrorOnce($"PC-Check: {name}", ex); }
        }

        private static PerformanceFinding Simple(string title, string detail, PerformanceFindingSeverity severity,
            string action, string target, string glyph) =>
            new(Localization.T(title), Localization.T(detail), severity, Localization.T(action), target, glyph);

        private static PerformanceFinding Finding(string title, string detail, PerformanceFindingSeverity severity,
            string action, string target, string glyph, params object[] args) =>
            new(Localization.T(title), Localization.F(detail, args), severity, Localization.T(action), target, glyph);
    }
}
