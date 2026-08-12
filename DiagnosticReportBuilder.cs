using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace WinVora
{
    internal static class DiagnosticReportBuilder
    {
        private static readonly Regex WindowsPath = new(@"[A-Za-z]:\\[^\r\n|]+", RegexOptions.Compiled);
        private static readonly Regex IpAddress = new(@"\b(?:\d{1,3}\.){3}\d{1,3}\b", RegexOptions.Compiled);
        private static readonly Regex MacAddress = new(@"\b(?:[0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}\b", RegexOptions.Compiled);

        public static string Build(SystemInfoSnapshot snapshot, string version, string log)
        {
            string sanitizedLog = Sanitize(log, snapshot);
            return new StringBuilder()
                .AppendLine("WinVora Supportbericht (anonymisiert)")
                .AppendLine($"App-Version: {version}")
                .AppendLine($"Windows: {snapshot.WindowsEdition} {snapshot.WindowsVersion} (Build {snapshot.BuildNumber})")
                .AppendLine($"Architektur: {snapshot.Architecture}")
                .AppendLine($"CPU: {snapshot.CpuName}")
                .AppendLine($"RAM: {snapshot.RamTotal}")
                .AppendLine($"BIOS-Version: {snapshot.BiosVersion}")
                .AppendLine($"TPM: {snapshot.TpmVersion}")
                .AppendLine($"Secure Boot: {snapshot.SecureBoot}")
                .AppendLine().AppendLine("--- WinVora-Protokoll ---")
                .AppendLine(sanitizedLog).ToString();
        }

        public static string Sanitize(string text, SystemInfoSnapshot snapshot)
        {
            foreach (string sensitive in new[] { snapshot.ComputerName, snapshot.UserName, snapshot.SerialNumber, Environment.UserName, Environment.MachineName })
                if (!string.IsNullOrWhiteSpace(sensitive)) text = text.Replace(sensitive, "[ANONYMISIERT]", StringComparison.OrdinalIgnoreCase);
            text = WindowsPath.Replace(text, "[PFAD ANONYMISIERT]");
            text = IpAddress.Replace(text, "[IP ANONYMISIERT]");
            text = MacAddress.Replace(text, "[MAC ANONYMISIERT]");
            return text;
        }
    }
}
