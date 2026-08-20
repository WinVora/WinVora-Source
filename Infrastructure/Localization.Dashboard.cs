using System.Collections.Generic;

namespace WinVora
{
    public static partial class Localization
    {
        private static IReadOnlyDictionary<string, (string De, string En)> GetDashboardStrings() =>
            new Dictionary<string, (string De, string En)>
            {
                ["Stat.Cpu"] = ("CPU", "CPU"),
                ["Stat.CpuLabel"] = ("Auslastung", "Usage"),
                ["Stat.Ram"] = ("RAM", "RAM"),
                ["Stat.Gpu"] = ("GPU", "GPU"),
                ["Stat.GpuLabel"] = ("Auslastung", "Usage"),
                ["Stat.Security"] = ("Sicherheit", "Security"),
                ["Stat.SecurityLabel"] = ("Defender / Firewall", "Defender / Firewall"),
                ["Stat.Updates"] = ("Updates", "Updates"),
                ["Stat.UpdatesLabel"] = ("Update-Pakete", "Update packages"),
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
                ["Dash.AllGood"] = ("Alles in Ordnung", "Everything looks good"),
                ["Dash.UsageHistory"] = ("Auslastung der letzten Minuten", "Usage over the last few minutes"),
                ["Dash.WindowsVersion"] = ("Windows-Version", "Windows version"),
                ["Dash.Detecting"] = ("Wird ermittelt...", "Detecting..."),
                ["Dash.PreparingComparison"] = ("Vergleich wird vorbereitet...", "Preparing comparison..."),
                ["Dash.CheckingUpdatesStatus"] = ("Updates werden geprüft", "Checking updates"),
                ["Dash.CheckingSecurityStatus"] = ("Sicherheit wird geprüft", "Checking security"),
                ["Dash.SystemMonitoring"] = ("Systemüberwachung läuft", "System monitoring running"),
                ["Dash.SystemPerformance"] = ("Systemleistung", "System performance"),
                ["Dash.SystemPerformanceDescription"] = ("Live-Werte für Prozessor, Arbeitsspeicher und Grafik", "Live values for processor, memory and graphics"),
                ["Dash.LoadError"] = ("Fehler beim Laden der Übersicht", "Error loading the dashboard")
            };
    }
}
