using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;

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

        public static bool SetEnabled(AutostartEntry entry, bool enabled)
        {
            try
            {
                string sourcePath = enabled ? DisabledKey : RunKey;
                string targetPath = enabled ? RunKey : DisabledKey;
                using var source = Registry.CurrentUser.OpenSubKey(sourcePath, writable: true);
                using var target = Registry.CurrentUser.CreateSubKey(targetPath, writable: true);
                string command = source?.GetValue(entry.Name)?.ToString() ?? entry.Command;
                target.SetValue(entry.Name, command, RegistryValueKind.String);
                source?.DeleteValue(entry.Name, throwOnMissingValue: false);
                return string.Equals(target.GetValue(entry.Name)?.ToString(), command, StringComparison.Ordinal);
            }
            catch (Exception ex)
            {
                Logger.LogError($"AutostartService.SetEnabled({entry.Name})", ex);
                return false;
            }
        }

        public static bool CommandTargetExists(string command)
            => TryGetCommandTargetPath(command, out string path) && File.Exists(path);

        public static bool TryGetCommandTargetPath(string command, out string path)
        {
            path = string.Empty;
            if (string.IsNullOrWhiteSpace(command)) return false;
            string expanded = Environment.ExpandEnvironmentVariables(command.Trim());
            path = expanded.StartsWith('"')
                ? expanded[1..].Split('"')[0]
                : Regex.Match(expanded, @"^.*?\.(exe|cmd|bat|com)(?=\s|$)", RegexOptions.IgnoreCase).Value;
            return !string.IsNullOrWhiteSpace(path);
        }

        public static (string Publisher, bool Signed) GetFileIdentity(string command)
        {
            if (!TryGetCommandTargetPath(command, out string path) || !File.Exists(path))
                return ("-", false);
            string publisher = FileVersionInfo.GetVersionInfo(path).CompanyName ?? "-";
            try
            {
                using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
                return (string.IsNullOrWhiteSpace(publisher) ? certificate.GetNameInfo(X509NameType.SimpleName, false) : publisher, true);
            }
            catch { return (publisher, false); }
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
