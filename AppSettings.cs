using System;
using System.IO;
using System.Text.Json;

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

        // Ob Ein-/Ausblend-Animationen beim Seitenwechsel reduziert werden sollen.
        public bool ReducedMotion { get; set; } = false;

        // Ob die Oberfläche im dunklen (true) oder hellen (false) Modus dargestellt wird.
        public bool DarkMode { get; set; } = true;

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

        // Sprache der Oberfläche: "de" oder "en".
        public string Language { get; set; } = "de";

        // Ob die Sprachauswahl beim allerersten Start schon gezeigt wurde.
        public bool HasChosenLanguage { get; set; } = false;

        // Verlauf der letzten Aktionen (Bereinigung, Updates, Deinstallation) -
        // wird auf der Dashboard-Seite als kleine Liste angezeigt.
        public System.Collections.Generic.List<ActivityLogEntry> ActivityLog { get; set; } = new();
        public System.Collections.Generic.List<DeferredUpdateEntry> DeferredUpdates { get; set; } = new();

        public int? WindowX { get; set; }
        public int? WindowY { get; set; }
        public int WindowWidth { get; set; } = 1280;
        public int WindowHeight { get; set; } = 800;
        public int? SettingsWindowX { get; set; }
        public int? SettingsWindowY { get; set; }
        public int SettingsWindowWidth { get; set; } = 460;
        public int SettingsWindowHeight { get; set; } = 620;
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
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                    if (loaded != null)
                    {
                        loaded.Validate();
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

        private void Validate()
        {
            string[] startupPages = { "Übersicht", "System", "Updates", "Storage", "Uninstall" };
            if (!Array.Exists(startupPages, page => string.Equals(page, StartupPage, StringComparison.Ordinal)))
                StartupPage = "Übersicht";

            int[] intervals = { 1, 2, 5, 10 };
            if (Array.IndexOf(intervals, LiveUpdateIntervalSeconds) < 0)
                LiveUpdateIntervalSeconds = 2;

            if (Language is not ("de" or "en")) Language = "de";
            GlassIntensity = Math.Clamp(GlassIntensity, 0, 64);
            WindowWidth = Math.Clamp(WindowWidth, 900, 3840);
            WindowHeight = Math.Clamp(WindowHeight, 650, 2160);
            SettingsWindowWidth = Math.Clamp(SettingsWindowWidth, 420, 1920);
            SettingsWindowHeight = Math.Clamp(SettingsWindowHeight, 480, 1440);
            ChangelogWindowWidth = Math.Clamp(ChangelogWindowWidth, 480, 1920);
            ChangelogWindowHeight = Math.Clamp(ChangelogWindowHeight, 520, 1440);
            ActivityLog ??= new();
            DeferredUpdates ??= new();
            DeferredUpdates.RemoveAll(entry => string.IsNullOrWhiteSpace(entry.PackageId));
        }

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(SettingsFilePath);
                if (dir != null) Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFilePath, json);
            }
            catch (Exception ex)
            {
                Logger.LogError("Einstellungen konnten nicht gespeichert werden", ex);
            }
        }
    }
}
