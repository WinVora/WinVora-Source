using System;
using System.Collections.Generic;

namespace WinVora
{
    internal sealed record PerformanceToolCommand(string FileName, string? Arguments = null);

    internal static class PerformanceActionCatalog
    {
        private static readonly IReadOnlyDictionary<string, PerformanceToolCommand> ExternalTools =
            new Dictionary<string, PerformanceToolCommand>(StringComparer.Ordinal)
            {
                ["TaskManager"] = new("taskmgr.exe"),
                ["DeviceManager"] = new("devmgmt.msc"),
                ["ReliabilityMonitor"] = new("perfmon.exe", "/rel"),
                ["EventViewerSystem"] = new("eventvwr.msc", "/c:System"),
                ["NetworkSettings"] = new("ms-settings:network-status"),
                ["PowerOptions"] = new("control.exe", "/name Microsoft.PowerOptions"),
                ["AdvancedSystemSettings"] = new("SystemPropertiesAdvanced.exe"),
                ["BatterySettings"] = new("ms-settings:batterysaver"),
                ["ActivationSettings"] = new("ms-settings:activation"),
                ["WindowsSecurity"] = new("windowsdefender:"),
                ["WindowsSystemInfo"] = new("msinfo32.exe"),
                ["DiskSettings"] = new("ms-settings:disksandvolumes")
            };

        public static bool TryGetExternalTool(string target, out PerformanceToolCommand command) =>
            ExternalTools.TryGetValue(target, out command!);
    }
}
