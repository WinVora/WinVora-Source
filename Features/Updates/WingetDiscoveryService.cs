using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WinVora
{
    internal sealed record WingetDiscoveryResult(List<WingetPackage> Packages, int[]? Columns);

    internal static class WingetDiscoveryService
    {
        private static readonly TimeSpan DiscoveryTimeout = TimeSpan.FromSeconds(75);

        public static async Task<WingetDiscoveryResult> GetUpgradesAsync(CancellationToken cancellationToken)
        {
            var result = await SystemAccess.ProcessRunner.RunAsync(
                new ProcessStartInfo
                {
                    FileName = "winget",
                    Arguments = "upgrade --disable-interactivity",
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                },
                DiscoveryTimeout,
                cancellationToken).ConfigureAwait(false);

            if (result.TimedOut)
                throw new TimeoutException($"WinGet hat innerhalb von {DiscoveryTimeout.TotalSeconds:0} Sekunden nicht geantwortet.");

            // Ein Nicht-Null-Exitcode ist immer ein fehlgeschlagener Aufruf.
            // Bereits geparste Zeilen dürfen einen Source-, Netzwerk- oder
            // Clientfehler nicht als erfolgreiche Teilliste maskieren.
            if (result.ExitCode != 0)
            {
                string details = string.IsNullOrWhiteSpace(result.StandardError)
                    ? result.StandardOutput.Trim()
                    : result.StandardError.Trim();
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(details)
                    ? $"WinGet wurde mit Code {result.ExitCode} beendet."
                    : $"WinGet wurde mit Code {result.ExitCode} beendet: {details}");
            }

            cancellationToken.ThrowIfCancellationRequested();
            return ParseOutput(result.StandardOutput, cancellationToken);
        }

        internal static WingetDiscoveryResult ParseOutput(
            string output,
            CancellationToken cancellationToken = default)
        {
            var packages = new List<WingetPackage>();
            string? headerLine = null;
            int[]? columns = null;

            bool hasRows = false;
            foreach (string line in output.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
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
