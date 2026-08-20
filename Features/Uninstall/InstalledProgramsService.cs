using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Principal;
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
        public long EstimatedSizeBytes { get; set; }
        public string UninstallString { get; set; } = "";
        public string QuietUninstallString { get; set; } = "";
        public string InstallLocation { get; set; } = "";
        public bool IsPerUserInstall { get; set; }

        // Aufgelöster Dateipfad (.exe oder .ico), aus dem das echte Icon extrahiert werden kann.
        // Leer, falls nichts Passendes gefunden wurde.
        public string IconPath { get; set; } = "";
        internal string DisplayIcon { get; set; } = "";
    }

    public static class InstalledProgramsService
    {
        public static List<InstalledProgram> GetInstalledPrograms(bool resolveIcons = true)
        {
            var results = new List<InstalledProgram>();

            var keysToScan = new (RegistryHive Hive, RegistryView View, string Path, bool IsPerUser)[]
            {
                (RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", false),
                (RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", false),
                (RegistryHive.CurrentUser, RegistryView.Default, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", true),
            };

            foreach (var (hive, view, path, isPerUser) in keysToScan)
            {
                try
                {
                    foreach (RegistryEntrySnapshot subKey in SystemAccess.Registry.ReadSubKeys(hive, view, path))
                    {
                        try
                        {
                            var displayName = subKey.Get("DisplayName") as string;
                            if (string.IsNullOrWhiteSpace(displayName)) continue;

                            // Systemkomponenten (keine echten, für Nutzer relevanten Programme) überspringen
                            var systemComponent = subKey.Get("SystemComponent");
                            if (systemComponent != null && Convert.ToInt32(systemComponent) == 1) continue;

                            // Windows-Updates/Hotfixes rausfiltern - keine "Programme" im eigentlichen Sinn
                            var releaseType = subKey.Get("ReleaseType") as string;
                            if (releaseType is "Update" or "Hotfix" or "SecurityUpdate") continue;

                            var uninstallString = subKey.Get("UninstallString") as string ?? "";
                            if (string.IsNullOrWhiteSpace(uninstallString)) continue; // nichts zum Deinstallieren da

                            var program = new InstalledProgram
                            {
                                DisplayName = displayName,
                                Publisher = subKey.Get("Publisher") as string ?? "Unbekannt",
                                Version = subKey.Get("DisplayVersion") as string ?? "",
                                UninstallString = uninstallString,
                                QuietUninstallString = subKey.Get("QuietUninstallString") as string ?? "",
                                IsPerUserInstall = isPerUser
                            };

                            var installDate = subKey.Get("InstallDate") as string;
                            if (!string.IsNullOrEmpty(installDate) && installDate.Length == 8 &&
                                DateTime.TryParseExact(installDate, "yyyyMMdd", CultureInfo.InvariantCulture,
                                    DateTimeStyles.None, out var dt))
                            {
                                program.InstallDate = dt.ToString("dd.MM.yyyy");
                            }

                            var estimatedSize = subKey.Get("EstimatedSize");
                            if (estimatedSize != null)
                            {
                                var kb = Convert.ToInt64(estimatedSize);
                                program.EstimatedSizeBytes = kb * 1024;
                                program.SizeDisplay = FormatBytes(kb * 1024);
                            }

                            var displayIcon = subKey.Get("DisplayIcon") as string;
                            var installLocation = subKey.Get("InstallLocation") as string;
                            program.InstallLocation = installLocation ?? "";
                            program.DisplayIcon = displayIcon ?? "";
                            program.IconPath = resolveIcons ? ResolveIconPath(displayIcon, installLocation) : "";

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

            // Nur tatsächlich gleiche Einträge zusammenführen. Programme mit
            // gleichem Anzeigenamen, aber anderer Version, anderem Pfad oder
            // anderem Deinstaller müssen als getrennte Installationen sichtbar
            // bleiben (z.B. parallele x86-/x64- oder Runtime-Versionen).
            return DeduplicatePrograms(results)
                .OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        internal static IEnumerable<InstalledProgram> DeduplicatePrograms(IEnumerable<InstalledProgram> programs) =>
            programs.GroupBy(BuildProgramIdentity, StringComparer.OrdinalIgnoreCase).Select(group => group.First());

        internal static string BuildProgramIdentity(InstalledProgram program)
        {
            static string Normalize(string? value) => (value ?? string.Empty).Trim().TrimEnd('\\', '/');
            return string.Join('|',
                Normalize(program.DisplayName),
                Normalize(program.Version),
                Normalize(program.InstallLocation),
                Normalize(program.UninstallString),
                program.IsPerUserInstall ? "user" : "machine");
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
            var match = FindProgramForName(installedPrograms, nameHint);

            return string.IsNullOrWhiteSpace(match?.IconPath) ? null : match.IconPath;
        }

        internal static InstalledProgram? FindProgramForName(List<InstalledProgram> installedPrograms, string nameHint) =>
            FindBestMatch(installedPrograms, nameHint, null);

        internal static string ResolveIconPathForProgram(InstalledProgram program)
        {
            if (!string.IsNullOrWhiteSpace(program.IconPath)) return program.IconPath;
            program.IconPath = ResolveIconPath(program.DisplayIcon, program.InstallLocation);
            return program.IconPath;
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
                if (!TryStartUninstaller(program, out Process? process, out string error))
                    return (false, error);
                if (process == null)
                    return (false, "Der Deinstaller konnte nicht gestartet werden.");
                process.Dispose();
                return (true, "Deinstallation gestartet - folge ggf. dem Assistenten in einem neuen Fenster.");
            }
            catch (Exception ex)
            {
                return (false, $"Fehler: {ex.Message}");
            }
        }

        public static async Task<(bool Success, string Message, bool WaitedForExit)> UninstallAndWaitAsync(
            InstalledProgram program,
            CancellationToken cancellationToken)
        {
            if (!TryStartUninstaller(program, out Process? process, out string error) || process == null)
                return (false, error, false);

            using (process)
            {
                var watch = Stopwatch.StartNew();
                try
                {
                    await process.WaitForExitAsync(cancellationToken);
                    bool meaningfulWait = watch.Elapsed >= TimeSpan.FromSeconds(2);
                    return (true,
                        meaningfulWait
                            ? "Der Hersteller-Deinstaller wurde geschlossen."
                            : "Der Deinstaller wurde gestartet und hat die Steuerung an einen weiteren Prozess übergeben.",
                        meaningfulWait);
                }
                catch (OperationCanceledException)
                {
                    return (true, "Die Überwachung wurde beendet; der Hersteller-Deinstaller kann weiterlaufen.", false);
                }
            }
        }

        private static bool TryStartUninstaller(
            InstalledProgram program,
            out Process? process,
            out string error)
        {
            process = null;
            error = "";
            try
            {
                string command = !string.IsNullOrWhiteSpace(program.QuietUninstallString)
                    ? program.QuietUninstallString
                    : program.UninstallString;
                if (string.IsNullOrWhiteSpace(command))
                {
                    error = "Für dieses Programm ist kein Deinstaller hinterlegt.";
                    return false;
                }

                // HKCU kann vom normalen Benutzer und damit auch von einem nicht
                // erhöhten Prozess verändert werden. Solange WinVora selbst noch
                // erhöht läuft, darf daraus kein Befehl mit Administratorrechten
                // gestartet werden. Nach der Umstellung des Hauptprozesses auf
                // asInvoker läuft dieser Zweig wieder normal ohne Erhöhung.
                if (program.IsPerUserInstall && IsCurrentProcessElevated())
                {
                    error = "Dieser benutzerspezifische Deinstaller wird aus Sicherheitsgründen nicht mit Administratorrechten gestartet. Öffne ihn vorerst über Windows > Installierte Apps.";
                    Logger.Log($"Erhöhter Start eines HKCU-Deinstallers blockiert: {program.DisplayName}");
                    return false;
                }

                string expandedCommand = Environment.ExpandEnvironmentVariables(command.Trim());
                if (!TrySplitCommand(expandedCommand, out string executable, out string arguments))
                {
                    error = "Der hinterlegte Deinstallationsbefehl hat ein unsicheres oder nicht unterstütztes Format.";
                    Logger.Log($"Unsicherer Deinstallationsbefehl blockiert: {program.DisplayName}");
                    return false;
                }

                string extension = Path.GetExtension(executable);
                if (!extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
                    !extension.Equals(".com", StringComparison.OrdinalIgnoreCase))
                {
                    error = "Skriptbasierte Deinstallationsbefehle werden aus Sicherheitsgründen nicht automatisch gestartet.";
                    Logger.Log($"Skriptbasierter Deinstallationsbefehl blockiert: {program.DisplayName} ({extension})");
                    return false;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = arguments,
                    UseShellExecute = true
                };

                process = SystemAccess.Process.Start(startInfo);
                if (process != null) return true;
                error = "Der Deinstaller konnte nicht gestartet werden.";
                return false;
            }
            catch (Exception ex)
            {
                error = $"Fehler: {ex.Message}";
                return false;
            }
        }

        private static bool IsCurrentProcessElevated()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch (Exception ex)
            {
                Logger.LogErrorOnce("Administratorstatus vor Deinstallation prüfen", ex);
                // Kann der Status nicht sicher bestimmt werden, ist Blockieren
                // für benutzerschreibbare HKCU-Befehle die sichere Variante.
                return true;
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

            var executableMatch = Regex.Match(command, @"^(.+?\.(?:exe|com))(?=\s|$)", RegexOptions.IgnoreCase);
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
