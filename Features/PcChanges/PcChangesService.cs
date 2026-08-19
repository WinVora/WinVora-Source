using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WinVora
{
    internal sealed class PcStateSnapshot
    {
        public DateTime CapturedUtc { get; set; }
        public Dictionary<string, string> Programs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> StartupEntries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, long> DriveFreeBytes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, long> WatchedFolderBytes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    internal sealed record StorageGrowth(string Name, string Path, long GrowthBytes, long CurrentBytes);

    internal sealed class PcChangeSummary
    {
        public DateTime? PreviousUtc { get; init; }
        public DateTime CurrentUtc { get; init; }
        public int InstalledPrograms { get; init; }
        public int RemovedPrograms { get; init; }
        public int UpdatedPrograms { get; init; }
        public int AddedStartupEntries { get; init; }
        public int RemovedStartupEntries { get; init; }
        public long FreeSpaceDifferenceBytes { get; init; }
        public IReadOnlyList<StorageGrowth> StorageGrowth { get; init; } = Array.Empty<StorageGrowth>();
        public bool HasBaseline => PreviousUtc.HasValue;
        public bool HasChanges => InstalledPrograms + RemovedPrograms + UpdatedPrograms + AddedStartupEntries +
                                  RemovedStartupEntries > 0 || Math.Abs(FreeSpaceDifferenceBytes) >= 100 * 1024 * 1024 ||
                                  StorageGrowth.Count > 0;
    }

    internal static class PcChangesService
    {
        private static string SnapshotPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinVora", "pc-state.json");

        public static async Task<(PcStateSnapshot Current, PcChangeSummary Summary)> CaptureAndCompareAsync(
            IReadOnlyCollection<InstalledProgram> installedPrograms,
            CancellationToken cancellationToken,
            IReadOnlyCollection<string>? customWatchedFolders = null,
            long growthWarningBytes = 1024L * 1024 * 1024)
        {
            var current = await Task.Run(() => Capture(installedPrograms, cancellationToken, customWatchedFolders), cancellationToken);
            PcStateSnapshot? previous = Load();
            var summary = Compare(previous, current, growthWarningBytes);
            Save(current);
            return (current, summary);
        }

        internal static PcChangeSummary Compare(PcStateSnapshot? previous, PcStateSnapshot current,
            long growthWarningBytes = 1024L * 1024 * 1024)
        {
            if (previous == null) return new PcChangeSummary { CurrentUtc = current.CapturedUtc };

            var installed = current.Programs.Keys.Except(previous.Programs.Keys, StringComparer.OrdinalIgnoreCase).Count();
            var removed = previous.Programs.Keys.Except(current.Programs.Keys, StringComparer.OrdinalIgnoreCase).Count();
            var updated = current.Programs.Count(pair => previous.Programs.TryGetValue(pair.Key, out string? oldVersion) &&
                                                         !string.Equals(oldVersion, pair.Value, StringComparison.OrdinalIgnoreCase));
            var growth = current.WatchedFolderBytes
                .Where(pair => previous.WatchedFolderBytes.TryGetValue(pair.Key, out long oldSize) && pair.Value - oldSize >= growthWarningBytes)
                .Select(pair => new StorageGrowth(Path.GetFileName(pair.Key.TrimEnd(Path.DirectorySeparatorChar)), pair.Key,
                    pair.Value - previous.WatchedFolderBytes[pair.Key], pair.Value))
                .OrderByDescending(item => item.GrowthBytes).ToList();

            long currentFree = current.DriveFreeBytes.Values.Sum();
            long previousFree = previous.DriveFreeBytes.Values.Sum();
            return new PcChangeSummary
            {
                CurrentUtc = current.CapturedUtc,
                PreviousUtc = previous.CapturedUtc,
                InstalledPrograms = installed,
                RemovedPrograms = removed,
                UpdatedPrograms = updated,
                AddedStartupEntries = current.StartupEntries.Except(previous.StartupEntries, StringComparer.OrdinalIgnoreCase).Count(),
                RemovedStartupEntries = previous.StartupEntries.Except(current.StartupEntries, StringComparer.OrdinalIgnoreCase).Count(),
                FreeSpaceDifferenceBytes = currentFree - previousFree,
                StorageGrowth = growth
            };
        }

        private static PcStateSnapshot Capture(IReadOnlyCollection<InstalledProgram> programs, CancellationToken token,
            IReadOnlyCollection<string>? customWatchedFolders)
        {
            var snapshot = new PcStateSnapshot { CapturedUtc = DateTime.UtcNow };
            foreach (var program in programs.Where(p => !string.IsNullOrWhiteSpace(p.DisplayName)))
            {
                string key = $"{program.DisplayName}|{program.Publisher}";
                snapshot.Programs[key] = program.Version ?? "";
            }
            foreach (var entry in AutostartService.GetEntries()) snapshot.StartupEntries.Add($"{entry.Name}|{entry.Command}");
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
                snapshot.DriveFreeBytes[drive.Name] = drive.AvailableFreeSpace;

            foreach (string folder in GetWatchedFolders().Concat(customWatchedFolders ?? Array.Empty<string>())
                         .Distinct(StringComparer.OrdinalIgnoreCase).Where(Directory.Exists))
            {
                token.ThrowIfCancellationRequested();
                snapshot.WatchedFolderBytes[folder] = GetFolderSize(folder, token);
            }
            return snapshot;
        }

        private static IEnumerable<string> GetWatchedFolders()
        {
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            yield return Path.Combine(profile, "Downloads");
            yield return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        private static long GetFolderSize(string root, CancellationToken token)
        {
            long total = 0;
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                string directory = pending.Pop();
                try
                {
                    foreach (string file in Directory.EnumerateFiles(directory))
                    {
                        try { total += new FileInfo(file).Length; }
                        catch (Exception ex) { Logger.LogErrorOnce("Dateigröße für PC-Veränderungen lesen", ex); }
                    }
                    foreach (string child in Directory.EnumerateDirectories(directory)) pending.Push(child);
                }
                catch (UnauthorizedAccessException)
                {
                    // Geschützte Verknüpfungen wie "Eigene Videos" werden
                    // übersprungen und sollen das normale Startprotokoll nicht füllen.
                }
                catch (IOException ex) { Logger.LogErrorOnce("Ordnergröße für PC-Veränderungen lesen", ex); }
            }
            return total;
        }

        private static PcStateSnapshot? Load()
        {
            try
            {
                return File.Exists(SnapshotPath)
                    ? JsonSerializer.Deserialize<PcStateSnapshot>(File.ReadAllText(SnapshotPath))
                    : null;
            }
            catch (Exception ex) { Logger.LogError("PC-Vergleichspunkt lesen", ex); return null; }
        }

        private static void Save(PcStateSnapshot snapshot)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SnapshotPath)!);
                string temporary = SnapshotPath + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot));
                File.Move(temporary, SnapshotPath, overwrite: true);
            }
            catch (Exception ex) { Logger.LogError("PC-Vergleichspunkt speichern", ex); }
        }
    }
}
