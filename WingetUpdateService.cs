using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace WinVora
{
    internal sealed class WingetUpdateService
    {
        private readonly Stopwatch _downloadWatch = new();
        private static readonly Regex ProgressWithPercentRegex = new(
            @"(\d{1,3})\s*%.*?([\d.,]+\s?[KMGT]?B)\s*/\s*([\d.,]+\s?[KMGT]?B)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex ProgressSizeOnlyRegex = new(
            @"([\d.,]+\s?[KMGT]?B)\s*/\s*([\d.,]+\s?[KMGT]?B)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public async Task<WingetUpdateResult> UpgradeAsync(
            string packageId,
            IProgress<WingetUpdateProgress> progress,
            CancellationToken cancellationToken)
        {
            try
            {
                _downloadWatch.Restart();
                var startInfo = CreateStartInfo(packageId);
                using var process = new Process { StartInfo = startInfo };
                process.Start();

                using var cancellationRegistration = cancellationToken.Register(() =>
                {
                    try
                    {
                        if (!process.HasExited)
                            process.Kill(entireProcessTree: true);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError($"Abbruch von winget für {packageId}", ex);
                    }
                });

                var standardOutput = new StringBuilder();
                var errorOutput = new StringBuilder();

                Task outputTask = ReadOutputAsync(process.StandardOutput, standardOutput, progress, cancellationToken);
                Task errorTask = ReadOutputAsync(process.StandardError, errorOutput, null, cancellationToken);

                progress.Report(new WingetUpdateProgress(WingetUpdatePhase.Waiting, "", null));
                await Task.WhenAll(outputTask, errorTask, process.WaitForExitAsync(cancellationToken));

                string combinedOutput = $"{standardOutput}\n{errorOutput}";
                bool restartRequired = WingetErrorTranslator.ContainsRestartRequired(combinedOutput) ||
                                       process.ExitCode is 3010 or 1641;
                WingetUpdateStatus status = cancellationToken.IsCancellationRequested
                    ? WingetUpdateStatus.Cancelled
                    : restartRequired
                        ? WingetUpdateStatus.RestartRequired
                        : process.ExitCode == 0
                            ? WingetUpdateStatus.Successful
                            : WingetUpdateStatus.Failed;

                if (process.ExitCode != 0)
                {
                    string error = errorOutput.ToString().Trim();
                    Logger.Log($"winget upgrade '{packageId}' beendet mit ExitCode {process.ExitCode}" +
                               (string.IsNullOrEmpty(error) ? "." : $": {error}"));
                }

                return new WingetUpdateResult(
                    status,
                    process.ExitCode,
                    WingetErrorTranslator.GetFriendlyMessage(process.ExitCode, combinedOutput, status),
                    restartRequired);
            }
            catch (OperationCanceledException)
            {
                return new WingetUpdateResult(
                    WingetUpdateStatus.Cancelled,
                    -1,
                    Localization.CurrentLanguage == "en" ? "Installer was cancelled." : "Installer wurde abgebrochen.",
                    false);
            }
            catch (Exception ex)
            {
                Logger.LogError($"WingetUpdateService.UpgradeAsync({packageId})", ex);
                bool cancelled = cancellationToken.IsCancellationRequested;
                return new WingetUpdateResult(
                    cancelled ? WingetUpdateStatus.Cancelled : WingetUpdateStatus.Failed,
                    -1,
                    cancelled
                        ? (Localization.CurrentLanguage == "en" ? "Installer was cancelled." : "Installer wurde abgebrochen.")
                        : ex.Message,
                    false);
            }
        }

        private static ProcessStartInfo CreateStartInfo(string packageId)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "winget",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            startInfo.ArgumentList.Add("upgrade");
            startInfo.ArgumentList.Add("--id");
            startInfo.ArgumentList.Add(packageId);
            startInfo.ArgumentList.Add("--exact");
            startInfo.ArgumentList.Add("--interactive");
            startInfo.ArgumentList.Add("--accept-package-agreements");
            startInfo.ArgumentList.Add("--accept-source-agreements");
            return startInfo;
        }

        private async Task ReadOutputAsync(
            System.IO.StreamReader reader,
            StringBuilder output,
            IProgress<WingetUpdateProgress>? progress,
            CancellationToken cancellationToken)
        {
            while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(line)) continue;

                output.AppendLine(line);
                if (progress != null)
                    ReportProgress(line, progress);
            }
        }

        private void ReportProgress(string line, IProgress<WingetUpdateProgress> progress)
        {
            WingetUpdatePhase phase = line.Contains("download", StringComparison.OrdinalIgnoreCase) ||
                                      line.Contains("herunter", StringComparison.OrdinalIgnoreCase)
                ? WingetUpdatePhase.Downloading
                : line.Contains("install", StringComparison.OrdinalIgnoreCase) ||
                  line.Contains("package", StringComparison.OrdinalIgnoreCase)
                    ? WingetUpdatePhase.Installing
                    : WingetUpdatePhase.Waiting;

            var withPercent = ProgressWithPercentRegex.Match(line);
            if (withPercent.Success)
            {
                double percent = double.Parse(withPercent.Groups[1].Value, CultureInfo.InvariantCulture);
                double downloaded = ParseSizeBytes(withPercent.Groups[2].Value);
                double total = ParseSizeBytes(withPercent.Groups[3].Value);
                double seconds = Math.Max(0.1, _downloadWatch.Elapsed.TotalSeconds);
                double bytesPerSecond = downloaded / seconds;
                string? speed = bytesPerSecond > 0 ? $"{FormatRate(bytesPerSecond)}/s" : null;
                string? eta = bytesPerSecond > 0 && total > downloaded
                    ? TimeSpan.FromSeconds((total - downloaded) / bytesPerSecond).ToString(@"mm\:ss")
                    : null;
                progress.Report(new WingetUpdateProgress(
                    WingetUpdatePhase.Downloading,
                    $"{withPercent.Groups[2].Value.Trim()} / {withPercent.Groups[3].Value.Trim()}",
                    percent,
                    speed,
                    eta));
                return;
            }

            var sizeOnly = ProgressSizeOnlyRegex.Match(line);
            if (sizeOnly.Success)
            {
                progress.Report(new WingetUpdateProgress(
                    WingetUpdatePhase.Downloading,
                    $"{sizeOnly.Groups[1].Value.Trim()} / {sizeOnly.Groups[2].Value.Trim()}",
                    null));
                return;
            }

            if (phase != WingetUpdatePhase.Waiting)
                progress.Report(new WingetUpdateProgress(phase, "", null));
        }

        private static double ParseSizeBytes(string value)
        {
            var match = Regex.Match(value.Trim(), @"([\d.,]+)\s*([KMGT]?B)", RegexOptions.IgnoreCase);
            if (!match.Success) return 0;
            string number = match.Groups[1].Value.Replace(',', '.');
            if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double amount)) return 0;
            double factor = match.Groups[2].Value.ToUpperInvariant() switch
            {
                "KB" => 1024d,
                "MB" => 1024d * 1024,
                "GB" => 1024d * 1024 * 1024,
                "TB" => 1024d * 1024 * 1024 * 1024,
                _ => 1d
            };
            return amount * factor;
        }

        private static string FormatRate(double bytesPerSecond) => bytesPerSecond >= 1024 * 1024
            ? $"{bytesPerSecond / 1024 / 1024:0.0} MB"
            : $"{bytesPerSecond / 1024:0.0} KB";
    }
}
