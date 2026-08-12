using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace WinVora
{
    public class InstalledProgram
    {
        public string DisplayName { get; set; } = "";
        public string Publisher { get; set; } = "";
        public string Version { get; set; } = "";
        public string InstallDate { get; set; } = "";
        public string SizeDisplay { get; set; } = "";
        public string UninstallString { get; set; } = "";
        public string QuietUninstallString { get; set; } = "";

        // Aufgelöster Dateipfad (.exe oder .ico), aus dem das echte Icon extrahiert werden kann.
        // Leer, falls nichts Passendes gefunden wurde.
        public string IconPath { get; set; } = "";
    }

    public static class InstalledProgramsService
    {
        public static List<InstalledProgram> GetInstalledPrograms()
        {
            var results = new List<InstalledProgram>();

            var keysToScan = new (RegistryKey Hive, string Path)[]
            {
                (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
                (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
                (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            };

            foreach (var (hive, path) in keysToScan)
            {
                try
                {
                    using var key = hive.OpenSubKey(path);
                    if (key == null) continue;

                    foreach (var subKeyName in key.GetSubKeyNames())
                    {
                        try
                        {
                            using var subKey = key.OpenSubKey(subKeyName);
                            if (subKey == null) continue;

                            var displayName = subKey.GetValue("DisplayName") as string;
                            if (string.IsNullOrWhiteSpace(displayName)) continue;

                            // Systemkomponenten (keine echten, für Nutzer relevanten Programme) überspringen
                            var systemComponent = subKey.GetValue("SystemComponent");
                            if (systemComponent != null && Convert.ToInt32(systemComponent) == 1) continue;

                            // Windows-Updates/Hotfixes rausfiltern - keine "Programme" im eigentlichen Sinn
                            var releaseType = subKey.GetValue("ReleaseType") as string;
                            if (releaseType is "Update" or "Hotfix" or "SecurityUpdate") continue;

                            var uninstallString = subKey.GetValue("UninstallString") as string ?? "";
                            if (string.IsNullOrWhiteSpace(uninstallString)) continue; // nichts zum Deinstallieren da

                            var program = new InstalledProgram
                            {
                                DisplayName = displayName,
                                Publisher = subKey.GetValue("Publisher") as string ?? "Unbekannt",
                                Version = subKey.GetValue("DisplayVersion") as string ?? "",
                                UninstallString = uninstallString,
                                QuietUninstallString = subKey.GetValue("QuietUninstallString") as string ?? ""
                            };

                            var installDate = subKey.GetValue("InstallDate") as string;
                            if (!string.IsNullOrEmpty(installDate) && installDate.Length == 8 &&
                                DateTime.TryParseExact(installDate, "yyyyMMdd", CultureInfo.InvariantCulture,
                                    DateTimeStyles.None, out var dt))
                            {
                                program.InstallDate = dt.ToString("dd.MM.yyyy");
                            }

                            var estimatedSize = subKey.GetValue("EstimatedSize");
                            if (estimatedSize != null)
                            {
                                var kb = Convert.ToInt64(estimatedSize);
                                program.SizeDisplay = FormatBytes(kb * 1024);
                            }

                            var displayIcon = subKey.GetValue("DisplayIcon") as string;
                            var installLocation = subKey.GetValue("InstallLocation") as string;
                            program.IconPath = ResolveIconPath(displayIcon, installLocation);

                            results.Add(program);
                        }
                        catch
                        {
                            // einzelnen kaputten/unlesbaren Eintrag überspringen, Rest trotzdem laden
                        }
                    }
                }
                catch
                {
                    // Hive/Pfad nicht zugreifbar - einfach weiter mit den anderen
                }
            }

            // Duplikate (gleicher Name, z.B. aus 32/64-Bit-Registry-Ansicht) zusammenführen
            return results
                .GroupBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // Versucht, für ein Programm anhand des (ungefähren) Namens einen Dateipfad
        // für die Icon-Extraktion zu finden. Wird von der Winget-Seite genutzt, um
        // echte Icons statt Platzhaltern anzuzeigen.
        public static string? FindIconPathForName(List<InstalledProgram> installedPrograms, string nameHint)
        {
            if (string.IsNullOrWhiteSpace(nameHint)) return null;

            var match = installedPrograms.FirstOrDefault(p =>
                string.Equals(p.DisplayName, nameHint, StringComparison.OrdinalIgnoreCase));

            match ??= installedPrograms.FirstOrDefault(p =>
                p.DisplayName.Contains(nameHint, StringComparison.OrdinalIgnoreCase) ||
                nameHint.Contains(p.DisplayName, StringComparison.OrdinalIgnoreCase));

            return string.IsNullOrWhiteSpace(match?.IconPath) ? null : match.IconPath;
        }

        private static string ResolveIconPath(string? displayIcon, string? installLocation)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(displayIcon))
                {
                    // DisplayIcon-Werte sehen oft so aus: "C:\Pfad\app.exe,0" - Index abschneiden
                    var path = displayIcon.Split(',')[0].Trim('"');
                    if (File.Exists(path)) return path;
                }

                if (!string.IsNullOrWhiteSpace(installLocation) && Directory.Exists(installLocation))
                {
                    var exe = Directory.EnumerateFiles(installLocation, "*.exe", SearchOption.TopDirectoryOnly)
                        .FirstOrDefault();
                    if (exe != null) return exe;
                }
            }
            catch
            {
                // Zugriffsfehler o.ä. - kein Icon-Pfad
            }

            return "";
        }

        public static (bool success, string message) Uninstall(InstalledProgram program)
        {
            try
            {
                var command = !string.IsNullOrWhiteSpace(program.QuietUninstallString)
                    ? program.QuietUninstallString
                    : program.UninstallString;

                // UninstallString ist z.B. "MsiExec.exe /X{GUID}" oder ein Pfad zu einem
                // Setup/Uninstall.exe. Über cmd /c ausführen, damit beide Formen funktionieren,
                // inkl. evtl. Anführungszeichen im Pfad.
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{command}\"",
                    UseShellExecute = true, // lässt ggf. UAC-Prompt des Deinstallers zu
                    CreateNoWindow = false
                };

                Process.Start(psi);
                return (true, "Deinstallation gestartet - folge ggf. dem Assistenten in einem neuen Fenster.");
            }
            catch (Exception ex)
            {
                return (false, $"Fehler: {ex.Message}");
            }
        }

        private static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double size = bytes;
            int unit = 0;

            while (size >= 1024 && unit < units.Length - 1)
            {
                size /= 1024;
                unit++;
            }

            return $"{size:0.#} {units[unit]}";
        }
    }
}
