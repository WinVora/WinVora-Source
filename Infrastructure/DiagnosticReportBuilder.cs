using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;
using System.Diagnostics;

namespace WinVora
{
    internal static class DiagnosticReportBuilder
    {
        private static readonly Regex WindowsPath = new(@"[A-Za-z]:\\[^\r\n|]+", RegexOptions.Compiled);
        private static readonly Regex IpAddress = new(@"\b(?:\d{1,3}\.){3}\d{1,3}\b", RegexOptions.Compiled);
        private static readonly Regex MacAddress = new(@"\b(?:[0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}\b", RegexOptions.Compiled);

        public static string Build(SystemInfoSnapshot snapshot, string version, string log)
        {
            string sanitizedLog = SelectRelevantLogLines(Sanitize(log, snapshot), 150);
            return new StringBuilder()
                .AppendLine("WinVora Supportbericht (anonymisiert)")
                .AppendLine($"Erstellt: {DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}")
                .AppendLine($"App-Version: {version}")
                .AppendLine($"Windows: {snapshot.WindowsEdition} {snapshot.WindowsVersion} (Build {snapshot.BuildNumber})")
                .AppendLine($"Architektur: {snapshot.Architecture}")
                .AppendLine($"CPU: {snapshot.CpuName}")
                .AppendLine($"RAM: {snapshot.RamTotal}")
                .AppendLine($"WinVora-Arbeitsspeicher: {Process.GetCurrentProcess().WorkingSet64 / 1024d / 1024d:0.0} MB")
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

        public static string SelectRelevantLogLines(string log, int maximumLines = 40)
        {
            string[] lines = log.Replace("\r", "").Split('\n');
            var relevant = lines.Where(line =>
                    line.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("fehler", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("exception", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("warning", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("abbruch", StringComparison.OrdinalIgnoreCase))
                .TakeLast(maximumLines)
                .ToList();
            if (relevant.Count == 0)
                relevant = lines.Where(line => !string.IsNullOrWhiteSpace(line)).TakeLast(Math.Min(12, maximumLines)).ToList();
            return string.Join(Environment.NewLine, relevant);
        }
    }
}
