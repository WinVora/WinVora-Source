using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WinVora
{
    internal sealed record WingetDiscoveryResult(List<WingetPackage> Packages, int[]? Columns);

    internal static class WingetDiscoveryService
    {
        public static Task<WingetDiscoveryResult> GetUpgradesAsync(CancellationToken cancellationToken) =>
            Task.Run(() => GetUpgrades(cancellationToken), cancellationToken);

        private static WingetDiscoveryResult GetUpgrades(CancellationToken cancellationToken)
        {
            var packages = new List<WingetPackage>();
            string? headerLine = null;
            int[]? columns = null;
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "winget",
                    Arguments = "upgrade --disable-interactivity",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                }
            };

            process.Start();
            var errorReadTask = process.StandardError.ReadToEndAsync();
            using var cancellationRegistration = cancellationToken.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (Exception ex) { Logger.LogErrorOnce("WinGet-Abfrage abbrechen", ex); }
            });

            bool hasRows = false;
            string? line;
            while ((line = process.StandardOutput.ReadLine()) != null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(line))
                {
                    if (hasRows) break;
                    continue;
                }
                if (line.TrimStart().StartsWith('-') && headerLine != null && columns == null)
                {
                    columns = GetColumnStarts(headerLine);
                    continue;
                }
                if (columns == null) { headerLine = line; continue; }
                if (!line.Contains("  ")) { if (hasRows) break; continue; }

                var package = WingetTableParser.Parse(line, columns);
                if (package != null && !string.IsNullOrWhiteSpace(package.Id))
                {
                    packages.Add(package);
                    hasRows = true;
                }
            }

            process.WaitForExit();
            cancellationToken.ThrowIfCancellationRequested();
            string error = errorReadTask.GetAwaiter().GetResult().Trim();
            if (process.ExitCode != 0 && packages.Count == 0 && !string.IsNullOrWhiteSpace(error))
                throw new InvalidOperationException($"WinGet wurde mit Code {process.ExitCode} beendet: {error}");

            return new WingetDiscoveryResult(packages, columns);
        }

        private static int[] GetColumnStarts(string header)
        {
            var starts = new List<int> { 0 };
            for (int i = 2; i < header.Length; i++)
            {
                if (header[i] != ' ' && header[i - 1] == ' ' && header[i - 2] == ' ')
                    starts.Add(i);
            }
            return starts.ToArray();
        }
    }
}
