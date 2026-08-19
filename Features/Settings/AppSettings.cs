using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Collections.Generic;
using System.Reflection;

namespace WinVora
{
    // Ein einzelner Eintrag im Aktivitätsverlauf auf dem Dashboard.
    public class ActivityLogEntry
    {
        public DateTime TimestampUtc { get; set; }
        public string IconGlyph { get; set; } = "\uE73E";
        public string TextDe { get; set; } = "";
        public string TextEn { get; set; } = "";
        public string? PackageId { get; set; }
        public string? OldVersion { get; set; }
        public string? NewVersion { get; set; }
        public string? Result { get; set; }
        public int? ExitCode { get; set; }
        public string? SessionId { get; set; }
        public string? DetailsDe { get; set; }
        public string? DetailsEn { get; set; }
    }

    public class DeferredUpdateEntry
    {
        public string PackageId { get; set; } = "";
        public DateTime? HiddenUntilUtc { get; set; }
    }

    public class AppSettings
    {
        // Alpha-Wert (0-64) für die Deckkraft der Glas-Karten. 24 = Standard.
        public int GlassIntensity { get; set; } = 18;

        // Ob das Mica-Backdrop des Fensters genutzt werden soll.
        public bool UseMica { get; set; } = true;

        // Ob Animationen in der App, einschließlich Ladebildschirm und
        // Seitenwechsel, reduziert werden sollen.
        [System.Text.Json.Serialization.JsonIgnore]
        public bool ReducedMotion => AnimationMode != "Full";

        // "Full", "Reduced" oder "Off". ReducedMotion bleibt für ältere
        // Einstellungsdateien kompatibel und wird daraus abgeleitet.
        public string AnimationMode { get; set; } = "Full";

        // "System", "Dark" oder "Light".
        public string ColorScheme { get; set; } = "System";

        // "Stable" installiert nur reguläre Releases. "Beta" darf zusätzlich
        // als Vorabversion markierte GitHub-Releases anbieten.
        public string UpdateChannel { get; set; } = "Stable";

        public System.Collections.Generic.List<string> WatchedFolders { get; set; } = new();
        public long StorageGrowthWarningBytes { get; set; } = 1024L * 1024 * 1024;

        // Ob WinVora automatisch mit Windows starten soll.
        public bool AutoStartWithWindows { get; set; } = false;

        // Welche Seite beim Start angezeigt wird: "Übersicht", "System", "Updates" oder "Storage".
        public string StartupPage { get; set; } = "Übersicht";

        // Intervall in Sekunden für die Live-CPU/RAM-Anzeige.
        public int LiveUpdateIntervalSeconds { get; set; } = 2;

        // Ob vor dem Löschen von Storage-Kategorien ein Bestätigungsdialog erscheint.
        public bool ShowDeleteConfirmations { get; set; } = true;

        // Zeitpunkt (UTC) der letzten erfolgreichen Speicher-Bereinigung. Null,
        // falls noch nie bereinigt wurde.
        public DateTime? LastCleanupUtc { get; set; }
        public DateTime? LastSeenPcChangesUtc { get; set; }

        // Sprache der Oberfläche: "de" oder "en".
        public string Language { get; set; } = "de";

        // Ob die Sprachauswahl beim allerersten Start schon gezeigt wurde.
        public bool HasChosenLanguage { get; set; } = false;

        // Verlauf der letzten Aktionen (Bereinigung, Updates, Deinstallation) -
        // wird auf der Dashboard-Seite als kleine Liste angezeigt.
        public System.Collections.Generic.List<ActivityLogEntry> ActivityLog { get; set; } = new();
        public System.Collections.Generic.List<DeferredUpdateEntry> DeferredUpdates { get; set; } = new();
        public System.Collections.Generic.List<string> IgnoredUpdateIds { get; set; } = new();
        public System.Collections.Generic.List<string> ElevatedUpdateIds { get; set; } = new();
        public System.Collections.Generic.List<string> ShutdownUpdateIds { get; set; } = new();
        public System.Collections.Generic.List<string> HiddenDashboardCards { get; set; } = new();
        public System.Collections.Generic.List<string> DashboardCardOrder { get; set; } = new()
        {
            "Updates", "Security", "Storage", "Cpu", "Ram", "Gpu"
        };
        public System.Collections.Generic.List<string> RecentCommands { get; set; } = new();

        public bool NotifyUpdateCompletion { get; set; } = true;
        public bool NotifyRestartRequired { get; set; } = true;
        public bool ConfirmDownloadsCleanup { get; set; } = true;
        public bool ConfirmRecycleBinCleanup { get; set; } = true;
        public bool ConfirmBrowserCleanup { get; set; } = true;
        public bool OfferUninstallLeftoverScan { get; set; } = true;

        public int? WindowX { get; set; }
        public int? WindowY { get; set; }
        public int WindowWidth { get; set; } = 1280;
        public int WindowHeight { get; set; } = 800;
        public int? SettingsWindowX { get; set; }
        public int? SettingsWindowY { get; set; }
        public int SettingsWindowWidth { get; set; } = 560;
        public int SettingsWindowHeight { get; set; } = 680;
        public int? ChangelogWindowX { get; set; }
        public int? ChangelogWindowY { get; set; }
        public int ChangelogWindowWidth { get; set; } = 560;
        public int ChangelogWindowHeight { get; set; } = 720;

        private static string SettingsFilePath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WinVora",
                "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    var json = File.ReadAllText(SettingsFilePath);
                    using var document = JsonDocument.Parse(json);
                    bool hasColorScheme = document.RootElement.TryGetProperty(nameof(ColorScheme), out _);
                    bool hasAnimationMode = document.RootElement.TryGetProperty(nameof(AnimationMode), out _);
                    bool? legacyDarkMode = document.RootElement.TryGetProperty("DarkMode", out var darkMode) &&
                                           darkMode.ValueKind is JsonValueKind.True or JsonValueKind.False
                        ? darkMode.GetBoolean()
                        : null;
                    bool? legacyReducedMotion = document.RootElement.TryGetProperty("ReducedMotion", out var reducedMotion) &&
                                                reducedMotion.ValueKind is JsonValueKind.True or JsonValueKind.False
                        ? reducedMotion.GetBoolean()
                        : null;
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                    if (loaded != null)
                    {
                        if (!hasColorScheme && legacyDarkMode.HasValue)
                            loaded.ColorScheme = legacyDarkMode.Value ? "Dark" : "Light";
                        if (!hasAnimationMode && legacyReducedMotion.HasValue)
                            loaded.AnimationMode = legacyReducedMotion.Value ? "Reduced" : "Full";
                        loaded.Validate();
                        if (ContainsLegacyOrUnknownProperties(document.RootElement))
                        {
                            loaded.Save();
                            Logger.Log("Veraltete oder unbekannte Einstellungen wurden aus settings.json entfernt.");
                        }
                        return loaded;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("Einstellungen konnten nicht geladen werden; Standardwerte werden verwendet", ex);
            }

            return new AppSettings();
        }

        internal void Validate()
        {
            string[] startupPages = { "Übersicht", "System", "Updates", "Storage", "Uninstall" };
            if (!Array.Exists(startupPages, page => string.Equals(page, StartupPage, StringComparison.Ordinal)))
                StartupPage = "Übersicht";

            int[] intervals = { 1, 2, 5, 10 };
            if (Array.IndexOf(intervals, LiveUpdateIntervalSeconds) < 0)
                LiveUpdateIntervalSeconds = 2;

            if (Language is not ("de" or "en")) Language = "de";
            if (ColorScheme is not ("System" or "Dark" or "Light"))
                ColorScheme = "System";
            if (UpdateChannel is not ("Stable" or "Beta")) UpdateChannel = "Stable";
            if (AnimationMode is not ("Full" or "Reduced" or "Off"))
                AnimationMode = "Full";
            GlassIntensity = Math.Clamp(GlassIntensity, 0, 64);
            WindowWidth = Math.Clamp(WindowWidth, 900, 3840);
            WindowHeight = Math.Clamp(WindowHeight, 650, 2160);
            SettingsWindowWidth = Math.Clamp(SettingsWindowWidth, 420, 1920);
            SettingsWindowHeight = Math.Clamp(SettingsWindowHeight, 480, 1440);
            ChangelogWindowWidth = Math.Clamp(ChangelogWindowWidth, 480, 1920);
            ChangelogWindowHeight = Math.Clamp(ChangelogWindowHeight, 520, 1440);
            ActivityLog ??= new();
            HiddenDashboardCards ??= new();
            DashboardCardOrder ??= new();
            RecentCommands ??= new();
            WatchedFolders ??= new();
            WatchedFolders = WatchedFolders.Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToList();
            StorageGrowthWarningBytes = Math.Clamp(StorageGrowthWarningBytes, 100L * 1024 * 1024, 100L * 1024 * 1024 * 1024);
            foreach (string key in new[] { "Updates", "Security", "Storage", "Cpu", "Ram", "Gpu" })
                if (!DashboardCardOrder.Contains(key, StringComparer.OrdinalIgnoreCase))
                    DashboardCardOrder.Add(key);
            DeferredUpdates ??= new();
            IgnoredUpdateIds ??= new();
            ElevatedUpdateIds ??= new();
            ShutdownUpdateIds ??= new();
            DeferredUpdates.RemoveAll(entry => string.IsNullOrWhiteSpace(entry.PackageId));
            IgnoredUpdateIds = IgnoredUpdateIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            ElevatedUpdateIds = ElevatedUpdateIds.Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            ShutdownUpdateIds = ShutdownUpdateIds.Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static string GetSettingsFilePath() => SettingsFilePath;

        private static bool ContainsLegacyOrUnknownProperties(JsonElement root)
        {
            var supported = typeof(AppSettings).GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property => property.GetCustomAttribute<System.Text.Json.Serialization.JsonIgnoreAttribute>() == null)
                .Select(property => property.Name)
                .ToHashSet(StringComparer.Ordinal);
            return root.EnumerateObject().Any(property => !supported.Contains(property.Name));
        }

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(SettingsFilePath);
                if (dir != null) Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                var tempPath = SettingsFilePath + ".tmp";
                File.WriteAllText(tempPath, json);
                if (File.Exists(SettingsFilePath))
                    File.Move(tempPath, SettingsFilePath, overwrite: true);
                else
                    File.Move(tempPath, SettingsFilePath);
            }
            catch (Exception ex)
            {
                Logger.LogError("Einstellungen konnten nicht gespeichert werden", ex);
            }
        }

        internal void SaveCopy(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
