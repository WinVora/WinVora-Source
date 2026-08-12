using Microsoft.Win32;
using System;
using System.Collections.Generic;

namespace WinVora
{
    internal sealed record AutostartEntry(string Name, string Command, bool Enabled);

    internal static class AutostartService
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string DisabledKey = @"Software\WinVora\DisabledStartup";

        public static List<AutostartEntry> GetEntries()
        {
            var result = new List<AutostartEntry>();
            ReadKey(RunKey, true, result);
            ReadKey(DisabledKey, false, result);
            result.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        public static void SetEnabled(AutostartEntry entry, bool enabled)
        {
            string sourcePath = enabled ? DisabledKey : RunKey;
            string targetPath = enabled ? RunKey : DisabledKey;
            using var source = Registry.CurrentUser.OpenSubKey(sourcePath, writable: true);
            using var target = Registry.CurrentUser.CreateSubKey(targetPath, writable: true);
            string command = source?.GetValue(entry.Name)?.ToString() ?? entry.Command;
            target.SetValue(entry.Name, command, RegistryValueKind.String);
            source?.DeleteValue(entry.Name, throwOnMissingValue: false);
        }

        private static void ReadKey(string path, bool enabled, List<AutostartEntry> result)
        {
            using var key = Registry.CurrentUser.OpenSubKey(path);
            if (key == null) return;
            foreach (string name in key.GetValueNames())
                result.Add(new AutostartEntry(name, key.GetValue(name)?.ToString() ?? "", enabled));
        }
    }
}
