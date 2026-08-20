using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WinVora
{
    internal sealed record RegistryEntrySnapshot(string Name, IReadOnlyDictionary<string, object?> Values)
    {
        public object? Get(string name) => Values.TryGetValue(name, out object? value) ? value : null;
    }

    internal interface IProcessAccess
    {
        Process? Start(ProcessStartInfo startInfo);
    }

    internal sealed record ProcessRunResult(
        int ExitCode,
        string StandardOutput,
        string StandardError,
        bool TimedOut);

    internal interface IProcessRunner
    {
        Task<ProcessRunResult> RunAsync(
            ProcessStartInfo startInfo,
            TimeSpan timeout,
            CancellationToken cancellationToken = default);
    }

    internal interface IFileSystemAccess
    {
        bool FileExists(string path);
        bool DirectoryExists(string path);
        FileAttributes GetAttributes(string path);
        IEnumerable<string> EnumerateFiles(string path, string pattern = "*");
        IEnumerable<string> EnumerateDirectories(string path);
    }

    internal interface IRegistryAccess
    {
        IReadOnlyList<RegistryEntrySnapshot> ReadSubKeys(RegistryHive hive, RegistryView view, string path);
    }

    internal interface IWmiAccess
    {
        IReadOnlyList<IReadOnlyDictionary<string, object?>> Query(
            string scope, string query, params string[] properties);
    }

    internal static class SystemAccess
    {
        internal static IProcessAccess Process { get; set; } = new WindowsProcessAccess();
        internal static IProcessRunner ProcessRunner { get; set; } = new WindowsProcessRunner();
        internal static IFileSystemAccess FileSystem { get; set; } = new WindowsFileSystemAccess();
        internal static IRegistryAccess Registry { get; set; } = new WindowsRegistryAccess();
        internal static IWmiAccess Wmi { get; set; } = new WindowsWmiAccess();

        internal static void ResetDefaults()
        {
            Process = new WindowsProcessAccess();
            ProcessRunner = new WindowsProcessRunner();
            FileSystem = new WindowsFileSystemAccess();
            Registry = new WindowsRegistryAccess();
            Wmi = new WindowsWmiAccess();
        }
    }

    internal sealed class WindowsProcessAccess : IProcessAccess
    {
        public Process? Start(ProcessStartInfo startInfo) => System.Diagnostics.Process.Start(startInfo);
    }

    internal sealed class WindowsProcessRunner : IProcessRunner
    {
        public async Task<ProcessRunResult> RunAsync(
            ProcessStartInfo startInfo,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(startInfo);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);

            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.StandardOutputEncoding ??= Encoding.UTF8;
            startInfo.StandardErrorEncoding ??= Encoding.UTF8;

            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
                throw new InvalidOperationException($"Prozess konnte nicht gestartet werden: {startInfo.FileName}");

            Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            using var timeoutSource = new CancellationTokenSource(timeout);
            using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeoutSource.Token);

            try
            {
                await process.WaitForExitAsync(linkedSource.Token).ConfigureAwait(false);
                string output = await outputTask.ConfigureAwait(false);
                string error = await errorTask.ConfigureAwait(false);
                return new ProcessRunResult(process.ExitCode, output, error, TimedOut: false);
            }
            catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                TryKillProcessTree(process);
                await WaitForTerminationAsync(process).ConfigureAwait(false);
                return new ProcessRunResult(-1, GetCompletedText(outputTask), GetCompletedText(errorTask), TimedOut: true);
            }
            catch (OperationCanceledException)
            {
                TryKillProcessTree(process);
                await WaitForTerminationAsync(process).ConfigureAwait(false);
                throw;
            }
        }

        private static void TryKillProcessTree(Process process)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                Logger.LogErrorOnce("Prozess nach Abbruch beenden", ex);
            }
        }

        private static async Task WaitForTerminationAsync(Process process)
        {
            try
            {
                using var gracePeriod = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await process.WaitForExitAsync(gracePeriod.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException)
            {
                Logger.LogErrorOnce("Auf Prozessende warten", ex);
            }
        }

        private static string GetCompletedText(Task<string> task) =>
            task.Status == TaskStatus.RanToCompletion ? task.Result : string.Empty;
    }

    internal sealed class WindowsFileSystemAccess : IFileSystemAccess
    {
        public bool FileExists(string path) => File.Exists(path);
        public bool DirectoryExists(string path) => Directory.Exists(path);
        public FileAttributes GetAttributes(string path) => File.GetAttributes(path);
        public IEnumerable<string> EnumerateFiles(string path, string pattern = "*") =>
            Directory.EnumerateFiles(path, pattern, SearchOption.TopDirectoryOnly);
        public IEnumerable<string> EnumerateDirectories(string path) =>
            Directory.EnumerateDirectories(path, "*", SearchOption.TopDirectoryOnly);
    }

    internal sealed class WindowsRegistryAccess : IRegistryAccess
    {
        public IReadOnlyList<RegistryEntrySnapshot> ReadSubKeys(RegistryHive hive, RegistryView view, string path)
        {
            var result = new List<RegistryEntrySnapshot>();
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
            using RegistryKey? key = baseKey.OpenSubKey(path);
            if (key == null) return result;

            foreach (string subKeyName in key.GetSubKeyNames())
            {
                try
                {
                    using RegistryKey? subKey = key.OpenSubKey(subKeyName);
                    if (subKey == null) continue;
                    var values = subKey.GetValueNames().ToDictionary(
                        name => name,
                        name => subKey.GetValue(name),
                        StringComparer.OrdinalIgnoreCase);
                    result.Add(new RegistryEntrySnapshot(subKeyName, values));
                }
                catch (Exception ex)
                {
                    Logger.LogErrorOnce($"Registry-Eintrag lesen: {path}\\{subKeyName}", ex);
                }
            }
            return result;
        }
    }

    internal sealed class WindowsWmiAccess : IWmiAccess
    {
        public IReadOnlyList<IReadOnlyDictionary<string, object?>> Query(
            string scope, string query, params string[] properties)
        {
            using var searcher = new ManagementObjectSearcher(scope, query);
            searcher.Options.Timeout = TimeSpan.FromSeconds(4);
            searcher.Options.ReturnImmediately = true;
            using ManagementObjectCollection collection = searcher.Get();
            var rows = new List<IReadOnlyDictionary<string, object?>>();
            foreach (ManagementObject item in collection)
            {
                var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (string property in properties)
                    values[property] = item[property];
                rows.Add(values);
            }
            return rows;
        }
    }
}
