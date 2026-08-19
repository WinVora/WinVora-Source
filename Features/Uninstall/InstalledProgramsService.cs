using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
        public string InstallLocation { get; set; } = "";

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
                            program.InstallLocation = installLocation ?? "";
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

        public static List<string> FindPotentialLeftovers(InstalledProgram program)
        {
            var leftovers = new List<string>();
            if (!string.IsNullOrWhiteSpace(program.InstallLocation) && Directory.Exists(program.InstallLocation))
                leftovers.Add($"Ordner: {program.InstallLocation}");

            foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                try
                {
                    using var run = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
                    if (run == null) continue;
                    foreach (string valueName in run.GetValueNames())
                    {
                        string value = run.GetValue(valueName)?.ToString() ?? "";
                        if (valueName.Contains(program.DisplayName, StringComparison.OrdinalIgnoreCase) ||
                            (!string.IsNullOrWhiteSpace(program.InstallLocation) && value.Contains(program.InstallLocation, StringComparison.OrdinalIgnoreCase)))
                            leftovers.Add($"Autostart: {valueName}");
                    }
                }
                catch (Exception ex) { Logger.LogErrorOnce("Autostartreste aus Registry lesen", ex); }
            }
            return leftovers.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        // Versucht, für ein Programm anhand des (ungefähren) Namens einen Dateipfad
        // für die Icon-Extraktion zu finden. Wird von der Winget-Seite genutzt, um
        // echte Icons statt Platzhaltern anzuzeigen.
        public static string? FindIconPathForName(List<InstalledProgram> installedPrograms, string nameHint)
        {
            var match = FindBestMatch(installedPrograms, nameHint, null);

            return string.IsNullOrWhiteSpace(match?.IconPath) ? null : match.IconPath;
        }

        // Winget-Manifeste enthalten häufig keine Größenangabe. In diesem Fall
        // können Herausgeber und installierte Größe aus dem Windows-Uninstall-
        // Eintrag des bereits installierten Programms übernommen werden.
        public static (string Publisher, string Size) FindDetailsForPackage(
            List<InstalledProgram> installedPrograms, string packageName, string packageId)
        {
            var match = FindBestMatch(installedPrograms, packageName, packageId);
            return match == null ? ("", "") : (match.Publisher, match.SizeDisplay);
        }

        private static InstalledProgram? FindBestMatch(
            List<InstalledProgram> installedPrograms, string nameHint, string? packageId)
        {
            if (string.IsNullOrWhiteSpace(nameHint)) return null;

            var match = installedPrograms.FirstOrDefault(p =>
                string.Equals(p.DisplayName, nameHint, StringComparison.OrdinalIgnoreCase));

            match ??= installedPrograms.FirstOrDefault(p =>
                p.DisplayName.Contains(nameHint, StringComparison.OrdinalIgnoreCase) ||
                nameHint.Contains(p.DisplayName, StringComparison.OrdinalIgnoreCase));

            if (match != null || string.IsNullOrWhiteSpace(packageId)) return match;

            // Als letzter Fallback den letzten aussagekräftigen Teil der Winget-ID
            // verwenden, z.B. "Microsoft.PowerToys" -> "PowerToys".
            var idHint = packageId.Split('.', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            if (string.IsNullOrWhiteSpace(idHint) || idHint.Length < 4) return null;

            return installedPrograms.FirstOrDefault(p =>
                p.DisplayName.Contains(idHint, StringComparison.OrdinalIgnoreCase) ||
                idHint.Contains(p.DisplayName, StringComparison.OrdinalIgnoreCase));
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
            catch (Exception ex)
            {
                Logger.LogErrorOnce("Ein Programmsymbol konnte nicht ermittelt werden", ex);
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

                if (string.IsNullOrWhiteSpace(command))
                    return (false, "Für dieses Programm ist kein Deinstaller hinterlegt.");

                var expandedCommand = Environment.ExpandEnvironmentVariables(command.Trim());
                ProcessStartInfo psi;

                // Direkter Start verhindert ein unnötiges, teilweise offen bleibendes
                // cmd-Fenster (z.B. bei Steam-URLs). Nur echte Shell-Ausdrücke
                // benötigen weiterhin cmd.exe als unsichtbaren Fallback.
                if (TrySplitCommand(expandedCommand, out string executable, out string arguments))
                {
                    psi = new ProcessStartInfo
                    {
                        FileName = executable,
                        Arguments = arguments,
                        UseShellExecute = true
                    };
                }
                else
                {
                    psi = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/d /s /c \"{expandedCommand}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                }

                if (Process.Start(psi) == null)
                    return (false, "Der Deinstaller konnte nicht gestartet werden.");
                return (true, "Deinstallation gestartet - folge ggf. dem Assistenten in einem neuen Fenster.");
            }
            catch (Exception ex)
            {
                return (false, $"Fehler: {ex.Message}");
            }
        }

        internal static bool TrySplitCommand(string command, out string executable, out string arguments)
        {
            executable = "";
            arguments = "";
            if (string.IsNullOrWhiteSpace(command)) return false;

            if (command.IndexOfAny(new[] { '&', '|', '<', '>' }) >= 0)
                return false;

            if (command[0] == '"')
            {
                int closingQuote = command.IndexOf('"', 1);
                if (closingQuote <= 1) return false;
                executable = command[1..closingQuote];
                arguments = command[(closingQuote + 1)..].Trim();
                return true;
            }

            var executableMatch = Regex.Match(command, @"^(.+?\.(?:exe|com|bat|cmd))(?=\s|$)", RegexOptions.IgnoreCase);
            if (!executableMatch.Success) return false;
            executable = executableMatch.Groups[1].Value.Trim();
            arguments = command[executableMatch.Length..].Trim();
            return true;
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
