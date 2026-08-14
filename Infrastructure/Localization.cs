using System.Collections.Generic;

namespace WinVora
{
    // Einfache, dictionary-basierte Übersetzung für die am meisten sichtbaren
    // Teile der App (Sidebar, Seitentitel, Dashboard, Schnellzugriff,
    // Einstellungen-Fenster). Tiefer liegende Bereiche (Systeminfo-Details,
    // Storage/Winget/Deinstaller-Interna, Changelog, Log-Meldungen) bleiben
    // bewusst vorerst Deutsch - das lässt sich bei Bedarf gezielt erweitern,
    // ohne die Architektur hier ändern zu müssen (einfach neue Keys ergänzen).
    public static class Localization
    {
        // Wird beim Start bzw. bei jedem Sprachwechsel gesetzt.
        public static string CurrentLanguage = "de";

        private static readonly Dictionary<string, (string De, string En)> Strings = new()
        {
            // ---- Sidebar-Navigation ----
            ["Nav.Dashboard"] = ("Dashboard", "Dashboard"),
            ["Nav.System"] = ("Systeminfo", "System Info"),
            ["Nav.Updates"] = ("Updates", "Updates"),
            ["Nav.Files"] = ("Dateien", "Files"),
            ["Nav.Uninstall"] = ("Deinstallieren", "Uninstall"),
            ["Nav.Settings"] = ("Einstellungen", "Settings"),
            ["Nav.Contact"] = ("Kontakt", "Contact"),
            ["Nav.Kofi"] = ("Ko-fi", "Ko-fi"),
            ["Nav.ChangelogHint"] = ("Changelog anzeigen", "View changelog"),

            // ---- Große Seiten-Überschrift ----
            ["PageTitle.Dashboard"] = ("Dashboard", "Dashboard"),
            ["PageTitle.System"] = ("Systeminfo", "System Info"),
            ["PageTitle.Updates"] = ("Programm-Updates", "Program Updates"),
            ["PageTitle.Storage"] = ("Dateien", "Files"),
            ["PageTitle.Uninstall"] = ("Deinstallieren", "Uninstall"),

            // ---- Statuskarten (Dashboard) ----
            ["Stat.Cpu"] = ("CPU", "CPU"),
            ["Stat.CpuLabel"] = ("Auslastung", "Usage"),
            ["Stat.Ram"] = ("RAM", "RAM"),
            ["Stat.Gpu"] = ("GPU", "GPU"),
            ["Stat.GpuLabel"] = ("Auslastung", "Usage"),
            ["Stat.Security"] = ("Sicherheit", "Security"),
            ["Stat.SecurityLabel"] = ("Defender / Firewall", "Defender / Firewall"),
            ["Stat.Updates"] = ("Updates", "Updates"),
            ["Stat.UpdatesLabel"] = ("Update-Pakete", "Update packages"),

            // ---- Live-Dashboard-Kacheln ----
            ["Dash.Header"] = ("Live-Dashboard", "Live Dashboard"),
            ["Dash.Disk"] = ("Speicherplatz", "Storage"),
            ["Dash.Gpu"] = ("GPU-Auslastung", "GPU Usage"),
            ["Dash.Temp"] = ("Temperatur", "Temperature"),
            ["Dash.Programs"] = ("Programme", "Programs"),
            ["Dash.Cleanup"] = ("Zuletzt bereinigt", "Last cleaned"),
            ["Dash.UpdatesAvailable"] = ("Verfügbare Updates", "Available updates"),
            ["Dash.Ram"] = ("Arbeitsspeicher", "Memory"),
            ["Dash.Status"] = ("Gesamtstatus", "Overall status"),
            ["Dash.HistoryHeader"] = ("Verlauf", "History"),
            ["Dash.ActivityHeader"] = ("Letzte Aktionen", "Recent Actions"),
            ["Dash.NotAvailable"] = ("Nicht verfügbar", "Not available"),
            ["Dash.Checking"] = ("Wird geprüft...", "Checking..."),
            ["Dash.AllUpToDate"] = ("Alles aktuell", "Everything up to date"),
            ["Dash.PleaseCheck"] = ("Bitte prüfen", "Please check"),

            // ---- Schnellzugriff ----
            ["Quick.Header"] = ("Schnellzugriff", "Quick Access"),
            ["Quick.Hardware"] = ("Hardware anzeigen", "View Hardware"),
            ["Quick.UpdatePrograms"] = ("Programme aktualisieren", "Update Programs"),
            ["Quick.Clean"] = ("System bereinigen", "Clean System"),
            ["Quick.Settings"] = ("Einstellungen öffnen", "Open Settings"),

            // ---- Einstellungen-Fenster ----
            ["Settings.Title"] = ("Einstellungen", "Settings"),
            ["Settings.WindowTitle"] = ("WinVora Einstellungen", "WinVora Settings"),
            ["Settings.Appearance"] = ("Darstellung", "Appearance"),
            ["Settings.LightMode"] = ("Heller Modus", "Light Mode"),
            ["Settings.ColorScheme"] = ("Farbschema", "Color scheme"),
            ["Settings.UseMica"] = ("Mica-Hintergrund verwenden", "Use Mica background"),
            ["Settings.Animations"] = ("App-Animationen", "App animations"),
            ["Settings.On"] = ("An", "On"),
            ["Settings.Off"] = ("Aus", "Off"),
            ["Settings.Behavior"] = ("Verhalten", "Behavior"),
            ["Settings.StartupPage"] = ("Startseite", "Startup Page"),
            ["Settings.UpdateInterval"] = ("Aktualisierungsintervall (CPU/RAM/GPU)", "Update Interval (CPU/RAM/GPU)"),
            ["Settings.AutoStart"] = ("Mit Windows starten", "Start with Windows"),
            ["Settings.DeleteConfirm"] = ("Bestätigung vor dem Löschen", "Confirm before deleting"),
            ["Settings.Language"] = ("Sprache", "Language"),
            ["Settings.LanguageLabel"] = ("Sprache der Oberfläche", "Interface Language"),
            ["Settings.Maintenance"] = ("Wartung", "Maintenance"),
            ["Settings.OpenLog"] = ("Log-Datei öffnen", "Open Log File"),
            ["Settings.ClearLog"] = ("Log leeren", "Clear Log"),
            ["Settings.CheckUpdate"] = ("Nach Updates suchen", "Check for Updates"),
            ["Settings.UpdateSection"] = ("Update", "Update"),
            ["Settings.UpdateNow"] = ("Jetzt aktualisieren", "Update Now"),
            ["Settings.ResetSettings"] = ("Einstellungen zurücksetzen", "Reset Settings"),
            ["Settings.Close"] = ("Schließen", "Close"),

            // ---- Changelog-Fenster (nur Fenster-Titel, Einträge bleiben Deutsch) ----
            ["Changelog.WindowTitle"] = ("WinVora Changelog", "WinVora Changelog"),

            // ---- Erststart Sprachauswahl ----
            ["FirstRun.Title"] = ("Sprache wählen", "Choose Language"),
            ["FirstRun.Message"] = ("In welcher Sprache soll WinVora angezeigt werden?",
                                     "In which language should WinVora be displayed?"),
            ["FirstRun.German"] = ("Deutsch", "German"),
            ["FirstRun.English"] = ("Englisch", "English"),

            // ---- Allgemein ----
            ["Common.SelectAll"] = ("Alle auswählen", "Select All"),
            ["Common.DeselectAll"] = ("Alle abwählen", "Deselect All"),
            ["Common.Refresh"] = ("Aktualisieren", "Refresh"),
            ["Common.None"] = ("Keine", "None"),
            ["Common.Checking"] = ("Prüfe...", "Checking..."),
            ["Common.Loading"] = ("Wird geladen...", "Loading..."),
            ["Common.LoadingSystemInfo"] = ("Systeminfos werden geladen...", "Loading system info..."),
            ["Common.CheckingUpdates"] = ("Updates werden geprüft...", "Checking for updates..."),

            // ---- Winget-Seite ----
            ["Winget.SearchPlaceholder"] = ("Update suchen...", "Search updates..."),
            ["Winget.Publisher"] = ("Herausgeber", "Publisher"),
            ["Winget.Size"] = ("Größe", "Size"),
            ["Winget.Loading"] = ("wird geladen...", "loading..."),

            // ---- Kontakt ----
            ["Contact.Body"] = (
                "Fragen, Feedback oder Bugs?\n\n" +
                "E-Mail: winvoraadmin@gmail.com\n" +
                "GitHub (Downloads): github.com/WinVora/WinVora-Releases\n" +
                "Website: winvora.github.io/WinVora-Releases\n" +
                "TikTok: tiktok.com/@winvora6\n\n" +
                "Bei Problemen gerne einen Blick in die Logdatei werfen\n" +
                "(Einstellungen -> Log-Datei öffnen) und mit anhängen.",
                "Questions, feedback or bugs?\n\n" +
                "Email: winvoraadmin@gmail.com\n" +
                "GitHub (Downloads): github.com/WinVora/WinVora-Releases\n" +
                "Website: winvora.github.io/WinVora-Releases\n" +
                "TikTok: tiktok.com/@winvora6\n\n" +
                "If you run into problems, feel free to check the log file\n" +
                "(Settings -> Open Log File) and attach it."
            ),

            // ---- Systeminfo-Seite: Action-Bar ----
            ["System.Refresh"] = ("Aktualisieren", "Refresh"),
            ["System.ExpandAll"] = ("Alles aufklappen", "Expand All"),
            ["System.CollapseAll"] = ("Alles einklappen", "Collapse All"),

            // ---- Systeminfo-Seite: Abschnitts-Überschriften ----
            ["System.Device"] = ("Gerät", "Device"),
            ["System.Os"] = ("Betriebssystem", "Operating System"),
            ["System.Cpu"] = ("Prozessor", "Processor"),
            ["System.Ram"] = ("Arbeitsspeicher", "Memory"),
            ["System.Board"] = ("Mainboard und BIOS", "Motherboard and BIOS"),
            ["System.Security"] = ("Sicherheit", "Security"),
            ["System.Gpu"] = ("Grafik", "Graphics"),
            ["System.Drives"] = ("Laufwerke", "Drives"),
            ["System.Network"] = ("Netzwerk", "Network"),
            ["System.Battery"] = ("Akku", "Battery"),

            // ---- Systeminfo-Seite: innere Karten-Überschriften ----
            ["System.Card.Device"] = ("Geräteinformationen", "Device Information"),
            ["System.Card.Os"] = ("Windows", "Windows"),
            ["System.Card.Cpu"] = ("CPU", "CPU"),
            ["System.Card.Ram"] = ("RAM", "RAM"),
            ["System.Card.Board"] = ("Hardware", "Hardware"),
            ["System.Card.Security"] = ("Sicherheitsstatus", "Security Status"),
            ["System.Card.Gpu"] = ("Grafikkarten", "Graphics Cards"),
            ["System.Card.Drives"] = ("Speicherlaufwerke", "Storage Drives"),
            ["System.Card.Network"] = ("Aktive Netzwerkadapter", "Active Network Adapters"),
            ["System.Card.Battery"] = ("Energie", "Power"),

            // ---- Winget-Seite: Action-Bar ----
            ["Winget.StartUpdate"] = ("Updates installieren", "Install updates"),

            // ---- Storage-Seite: Gruppen + Action-Bar ----
            ["Storage.TempFiles"] = ("Temporäre Dateien", "Temporary Files"),
            ["Storage.RecycleDownloads"] = ("Papierkorb & Downloads", "Recycle Bin & Downloads"),
            ["Storage.SystemCaches"] = ("System-Caches", "System Caches"),
            ["Storage.ErrorLogs"] = ("Fehlerberichte & Logs", "Error Reports & Logs"),
            ["Storage.Browser"] = ("Browser", "Browser"),
            ["Storage.DeleteSelected"] = ("Alle ausgewählten löschen", "Delete All Selected"),

            // ---- Deinstaller-Seite ----
            ["Uninstall.SearchPlaceholder"] = ("Programm suchen...", "Search program..."),
        };

        public static string T(string key)
        {
            if (!Strings.TryGetValue(key, out var value))
                return key; // Fallback: Key selbst zeigen, falls Übersetzung fehlt

            return CurrentLanguage == "en" ? value.En : value.De;
        }

        // Feldbezeichnungen auf der Systeminfo-Seite (SysLbl01-SysLbl26).
        // Index 0 = SysLbl01, Index 1 = SysLbl02, usw.
        public static readonly (string De, string En)[] SystemFieldLabels =
        {
            ("Computername", "Computer Name"),
            ("Benutzername", "User Name"),
            ("Hersteller / Modell", "Manufacturer / Model"),
            ("Seriennummer", "Serial Number"),
            ("Architektur", "Architecture"),
            ("Windows Edition", "Windows Edition"),
            ("Version / Build", "Version / Build"),
            ("Installationsdatum", "Installation Date"),
            ("Letztes Windows Update", "Last Windows Update"),
            ("Aktivierungsstatus", "Activation Status"),
            ("Uptime", "Uptime"),
            (".NET Version", ".NET Version"),
            ("DirectX Version", "DirectX Version"),
            ("Modell", "Model"),
            ("Kerne / Threads / Takt", "Cores / Threads / Clock"),
            ("Auslastung Live", "Live Usage"),
            ("Installiert / Belegt / Frei", "Installed / Used / Free"),
            ("Auslastung Live", "Live Usage"),
            ("Mainboard", "Motherboard"),
            ("BIOS-Version", "BIOS Version"),
            ("Secure Boot", "Secure Boot"),
            ("TPM", "TPM"),
            ("Virtualisierung", "Virtualization"),
            ("Windows Defender", "Windows Defender"),
            ("Firewall", "Firewall"),
            ("BitLocker", "BitLocker"),
        };
    }
}
