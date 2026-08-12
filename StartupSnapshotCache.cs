using System;
using System.IO;
using System.Text.Json;

namespace WinVora
{
    internal static class StartupSnapshotCache
    {
        private sealed class CacheDocument
        {
            public DateTime SavedUtc { get; set; }
            public SystemInfoSnapshot Snapshot { get; set; } = new();
        }

        private static string CachePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinVora",
            "system-snapshot-cache.json");

        public static bool TryLoad(out SystemInfoSnapshot snapshot, out DateTime savedUtc)
        {
            snapshot = new SystemInfoSnapshot();
            savedUtc = default;
            try
            {
                if (!File.Exists(CachePath)) return false;
                var document = JsonSerializer.Deserialize<CacheDocument>(File.ReadAllText(CachePath));
                if (document?.Snapshot == null || document.SavedUtc == default) return false;
                snapshot = document.Snapshot;
                savedUtc = document.SavedUtc;
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError("Startcache lesen", ex);
                return false;
            }
        }

        public static void Save(SystemInfoSnapshot snapshot)
        {
            try
            {
                string? directory = Path.GetDirectoryName(CachePath);
                if (directory != null) Directory.CreateDirectory(directory);
                string temporaryPath = CachePath + ".tmp";
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(new CacheDocument
                {
                    SavedUtc = DateTime.UtcNow,
                    Snapshot = snapshot
                }));
                File.Move(temporaryPath, CachePath, overwrite: true);
            }
            catch (Exception ex)
            {
                Logger.LogError("Startcache speichern", ex);
            }
        }
    }
}
