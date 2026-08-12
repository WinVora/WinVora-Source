using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace WinVora
{
    public enum StorageActionType
    {
        Folder,        // Inhalt eines oder mehrerer Ordner wird gelöscht
        RecycleBin,    // Papierkorb leeren
        DnsFlush,      // ipconfig /flushdns
        StoreCacheReset // wsreset.exe
    }

    public class StorageCategory
    {
        public string Key { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string[] Paths { get; set; } = Array.Empty<string>();
        public string FilePattern { get; set; } = "*";
        public StorageActionType ActionType { get; set; } = StorageActionType.Folder;
        public bool RequiresAdmin { get; set; } = false;

        public long SizeBytes { get; set; }
        public string SizeDisplay { get; set; } = "Wird berechnet...";
    }

    public static class StorageService
    {
        public static List<StorageCategory> GetCategoryDefinitions()
        {
            string temp = Path.GetTempPath().TrimEnd('\\');
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string systemDrive = Path.GetPathRoot(windows) ?? "C:\\";
            bool en = Localization.CurrentLanguage == "en";

            return new List<StorageCategory>
            {
                new StorageCategory
                {
                    Key = "user_temp",
                    Name = en ? "User Temp" : "Benutzer Temp",
                    Description = en ? "Temporary files of the current user account" : "Temporäre Dateien des aktuellen Benutzerkontos",
                    Paths = new[] { temp }
                },
                new StorageCategory
                {
                    Key = "windows_temp",
                    Name = en ? "Windows Temp" : "Windows Temp",
                    Description = en ? "Temporary system files from Windows" : "Temporäre Systemdateien von Windows",
                    Paths = new[] { Path.Combine(windows, "Temp") },
                    RequiresAdmin = true
                },
                new StorageCategory
                {
                    Key = "prefetch",
                    Name = en ? "Prefetch" : "Prefetch",
                    Description = en ? "Cached startup data for faster program launches" : "Zwischengespeicherte Startdaten für schnelleren Programmstart",
                    Paths = new[] { Path.Combine(windows, "Prefetch") },
                    RequiresAdmin = true
                },
                new StorageCategory
                {
                    Key = "recycle_bin",
                    Name = en ? "Recycle Bin" : "Papierkorb",
                    Description = en ? "Permanently remove deleted files from the Recycle Bin" : "Endgültig gelöschte Dateien aus dem Papierkorb entfernen",
                    ActionType = StorageActionType.RecycleBin
                },
                new StorageCategory
                {
                    Key = "dx_shader_cache",
                    Name = en ? "DirectX Shader Cache" : "DirectX Shader Cache",
                    Description = en ? "Cached graphics shaders" : "Zwischengespeicherte Grafik-Shader",
                    Paths = new[] { Path.Combine(localAppData, "D3DSCache") }
                },
                new StorageCategory
                {
                    Key = "update_cache",
                    Name = en ? "Windows Update Cache" : "Windows Update Cache",
                    Description = en ? "Downloaded update files (including old, no longer needed downloads)" : "Heruntergeladene Update-Dateien (inkl. alter, nicht mehr benötigter Downloads)",
                    Paths = new[] { Path.Combine(windows, "SoftwareDistribution", "Download") },
                    RequiresAdmin = true
                },
                new StorageCategory
                {
                    Key = "delivery_optimization",
                    Name = en ? "Delivery Optimization Files" : "Delivery Optimization Dateien",
                    Description = en ? "Cached update chunks for peer distribution" : "Zwischengespeicherte Update-Teile für die Peer-Verteilung",
                    Paths = new[] { Path.Combine(programData, "Microsoft", "Network", "Downloader") },
                    RequiresAdmin = true
                },
                new StorageCategory
                {
                    Key = "wer",
                    Name = en ? "Windows Error Reporting" : "Windows Error Reporting",
                    Description = en ? "Crash reports sent to Microsoft or stored locally" : "Absturzberichte, die an Microsoft gesendet oder lokal gespeichert wurden",
                    Paths = new[] { Path.Combine(programData, "Microsoft", "Windows", "WER") },
                    RequiresAdmin = true
                },
                new StorageCategory
                {
                    Key = "minidump",
                    Name = en ? "MiniDump Files" : "MiniDump Dateien",
                    Description = en ? "Small crash memory dumps from system crashes (blue screens)" : "Kleine Absturz-Speicherabbilder von Systemabstürzen (Bluescreens)",
                    Paths = new[] { Path.Combine(windows, "Minidump") },
                    RequiresAdmin = true
                },
                new StorageCategory
                {
                    Key = "crash_dumps",
                    Name = en ? "Crash Dumps" : "Crash Dumps",
                    Description = en ? "Full crash memory dumps from applications" : "Vollständige Absturz-Speicherabbilder von Anwendungen",
                    Paths = new[] { Path.Combine(localAppData, "CrashDumps") }
                },
                new StorageCategory
                {
                    Key = "thumbnail_cache",
                    Name = en ? "Thumbnail Cache" : "Thumbnail Cache",
                    Description = en ? "Cached thumbnail previews from Explorer" : "Zwischengespeicherte Miniaturansichten des Explorers",
                    Paths = new[] { Path.Combine(localAppData, "Microsoft", "Windows", "Explorer") },
                    FilePattern = "thumbcache_*.db"
                },
                new StorageCategory
                {
                    Key = "browser_cache",
                    Name = en ? "Browser Cache" : "Browser Cache",
                    Description = en ? "Cache from Chrome and Edge" : "Zwischenspeicher von Chrome und Edge",
                    Paths = new[]
                    {
                        Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default", "Cache"),
                        Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default", "Cache")
                    }
                },
                new StorageCategory
                {
                    Key = "logs",
                    Name = en ? "Logs" : "Logs",
                    Description = en ? "General Windows log files" : "Allgemeine Windows-Protokolldateien",
                    Paths = new[] { Path.Combine(windows, "Logs") },
                    RequiresAdmin = true
                },
                new StorageCategory
                {
                    Key = "setup_logs",
                    Name = en ? "Setup Logs" : "Setup Logs",
                    Description = en ? "Logs from Windows Setup and feature updates" : "Protokolle von Windows-Setup und Feature-Updates",
                    Paths = new[] { Path.Combine(windows, "Panther") },
                    RequiresAdmin = true
                },
                new StorageCategory
                {
                    Key = "defender_temp",
                    Name = en ? "Defender Temporary Files" : "Defender temporäre Dateien",
                    Description = en ? "Temporary support and log files from Windows Defender" : "Temporäre Support- und Protokolldateien von Windows Defender",
                    Paths = new[] { Path.Combine(programData, "Microsoft", "Windows Defender", "Support") },
                    RequiresAdmin = true
                },
                new StorageCategory
                {
                    Key = "inet_cache",
                    Name = en ? "Temporary Internet Files" : "Temporary Internet Files",
                    Description = en ? "Cached web content from Internet Explorer / WebView" : "Zwischengespeicherte Webinhalte des Internet Explorer / WebView",
                    Paths = new[] { Path.Combine(localAppData, "Microsoft", "Windows", "INetCache") }
                },
                new StorageCategory
                {
                    Key = "store_cache",
                    Name = en ? "Microsoft Store Cache" : "Microsoft Store Cache",
                    Description = en ? "Resets the Microsoft Store cache" : "Setzt den Zwischenspeicher des Microsoft Store zurück",
                    ActionType = StorageActionType.StoreCacheReset
                },
                new StorageCategory
                {
                    Key = "dns_cache",
                    Name = en ? "DNS Cache" : "DNS Cache",
                    Description = en ? "Cached DNS resolutions" : "Zwischengespeicherte DNS-Auflösungen",
                    ActionType = StorageActionType.DnsFlush
                },
                new StorageCategory
                {
                    Key = "upgrade_logs",
                    Name = en ? "Windows Upgrade Logs" : "Windows Upgrade Logs",
                    Description = en ? "Logs from a previous Windows feature update" : "Protokolle eines vorherigen Windows-Feature-Updates",
                    Paths = new[] { Path.Combine(systemDrive, "$WINDOWS.~BT") },
                    RequiresAdmin = true
                },
                new StorageCategory
                {
                    Key = "old_install_files",
                    Name = en ? "No Longer Needed Installation Files" : "Nicht mehr benötigte Installationsdateien",
                    Description = en ? "Old Windows installation after an upgrade (Windows.old) - requires admin rights, may not be fully deletable" : "Alte Windows-Installation nach einem Upgrade (Windows.old) - benötigt Admin-Rechte, ggf. nicht vollständig löschbar",
                    Paths = new[] { Path.Combine(systemDrive, "Windows.old") },
                    RequiresAdmin = true
                },
            };
        }

        public static async Task<List<StorageCategory>> GetCategoriesWithSizesAsync()
        {
            var categories = GetCategoryDefinitions();

            var tasks = categories.Select(c => Task.Run(() =>
            {
                c.SizeBytes = c.ActionType switch
                {
                    StorageActionType.RecycleBin => GetRecycleBinSize(),
                    StorageActionType.DnsFlush => 0,
                    StorageActionType.StoreCacheReset => 0,
                    _ => c.Paths.Sum(p => GetDirectorySize(p, c.FilePattern))
                };

                c.SizeDisplay = c.ActionType is StorageActionType.DnsFlush or StorageActionType.StoreCacheReset
                    ? "Aktion"
                    : FormatBytes(c.SizeBytes);
            }));

            await Task.WhenAll(tasks);

            return categories;
        }

        public static async Task<(bool success, string message)> DeleteCategoryAsync(StorageCategory category)
        {
            return await Task.Run(() =>
            {
                switch (category.ActionType)
                {
                    case StorageActionType.RecycleBin:
                        return EmptyRecycleBin();

                    case StorageActionType.DnsFlush:
                        return RunHiddenCommand("ipconfig", "/flushdns");

                    case StorageActionType.StoreCacheReset:
                        return RunHiddenCommand("wsreset.exe", "");

                    default:
                        // BUGFIX: Windows.old UND $WINDOWS.~BT (Windows Upgrade
                        // Logs) gehören TrustedInstaller - normales
                        // File.Delete/Directory.Delete schlägt dort so gut wie
                        // immer fehl, selbst mit Admin-Rechten. Wir übernehmen
                        // vorher gezielt den Besitz und setzen die Rechte.
                        if (category.Key is "old_install_files" or "upgrade_logs")
                        {
                            return DeleteProtectedFolder(category.Paths.FirstOrDefault() ?? "");
                        }

                        int deleted = 0, failed = 0;
                        foreach (var path in category.Paths)
                        {
                            var (d, f) = DeleteDirectoryContents(path, category.FilePattern);
                            deleted += d;
                            failed += f;
                        }

                        if (failed == 0)
                            return (true, $"{deleted} Element(e) gelöscht.");

                        return (deleted > 0,
                            $"{deleted} Element(e) gelöscht, {failed} übersprungen (in Benutzung oder keine Berechtigung).");
                }
            });
        }

        // ================= SIZE CALCULATION =================

        private static long GetDirectorySize(string path, string pattern)
        {
            if (!Directory.Exists(path)) return 0;

            long size = 0;

            try
            {
                foreach (var file in Directory.EnumerateFiles(path, pattern, SearchOption.AllDirectories))
                {
                    try { size += new FileInfo(file).Length; }
                    catch { /* Datei nicht zugreifbar - ignorieren */ }
                }
            }
            catch { /* Ordner nicht zugreifbar - ignorieren */ }

            return size;
        }

        private static long GetRecycleBinSize()
        {
            try
            {
                var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
                int hr = SHQueryRecycleBin(null, ref info);
                return hr == 0 ? info.i64Size : 0;
            }
            catch
            {
                return 0;
            }
        }

        // ================= DELETION =================

        private static (int deleted, int failed) DeleteDirectoryContents(string path, string pattern)
        {
            int deleted = 0, failed = 0;
            if (!Directory.Exists(path)) return (0, 0);

            if (pattern != "*")
            {
                foreach (var file in SafeEnumerateFiles(path, pattern))
                {
                    try
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                        File.Delete(file);
                        deleted++;
                    }
                    catch { failed++; }
                }
                return (deleted, failed);
            }

            foreach (var file in SafeEnumerateFiles(path, "*"))
            {
                try
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                    deleted++;
                }
                catch { failed++; }
            }

            try
            {
                foreach (var dir in Directory.EnumerateDirectories(path, "*", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        Directory.Delete(dir, true);
                        deleted++;
                    }
                    catch { failed++; }
                }
            }
            catch { /* Zugriff auf Ordnerliste verweigert */ }

            return (deleted, failed);
        }

        private static IEnumerable<string> SafeEnumerateFiles(string path, string pattern)
        {
            try
            {
                return Directory.EnumerateFiles(path, pattern, SearchOption.TopDirectoryOnly).ToList();
            }
            catch
            {
                return Enumerable.Empty<string>();
            }
        }

        // Für Ordner, die (wie Windows.old) TrustedInstaller gehören:
        // erst Besitz übernehmen, dann Vollzugriff für Administratoren
        // setzen, erst dann löschen. Auch dann kann es ohne aktive
        // Admin-Rechte fehlschlagen - das wird sauber zurückgemeldet.
        private static (bool success, string message) DeleteProtectedFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return (true, "Nicht vorhanden, nichts zu löschen.");

            if (!IsAllowedProtectedFolder(path))
            {
                Logger.Log($"Geschützte Löschung aus Sicherheitsgründen abgelehnt: {path}");
                return (false, "Der angeforderte Systempfad ist nicht für die geschützte Bereinigung freigegeben.");
            }

            try
            {
                RunHiddenCommand("takeown.exe", $"/F \"{path}\" /R /D Y");
                // S-1-5-32-544 = lokale Gruppe "Administratoren" (sprachunabhängig)
                RunHiddenCommand("icacls.exe", $"\"{path}\" /grant *S-1-5-32-544:F /T /C /Q");

                // BUGFIX: Directory.Delete(path, true) ist alles-oder-nichts -
                // eine einzige besonders geschützte Datei (z.B. "bcd", die
                // Windows fürs Rückgängig-Machen des Updates braucht) lässt
                // sich selbst mit Admin-Rechten nicht löschen und ließ dadurch
                // bisher ALLES scheitern. Jetzt wird Datei für Datei einzeln
                // versucht, geschützte Einzeldateien werden einfach übersprungen.
                var (deleted, failed) = DeleteRecursiveBestEffort(path);

                // Versuchen, den (jetzt hoffentlich leeren oder fast leeren)
                // Ordner selbst noch zu entfernen - schlägt das fehl, ist das
                // kein Beinbruch, der Inhalt ist ja trotzdem größtenteils weg.
                try { Directory.Delete(path, true); } catch { /* Rest bleibt liegen, ok */ }

                if (failed == 0)
                    return (true, $"{deleted} Element(e) entfernt.");

                return (deleted > 0,
                    $"{deleted} Element(e) entfernt, {failed} geschützte Datei(en) übersprungen (z.B. Boot-Konfigurationsdaten).");
            }
            catch (Exception ex)
            {
                return (false,
                    $"Konnte nicht entfernt werden (Admin-Rechte erforderlich): {ex.Message}");
            }
        }

        private static bool IsAllowedProtectedFolder(string path)
        {
            try
            {
                var windowsPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                var systemRoot = Path.GetPathRoot(windowsPath);
                if (string.IsNullOrWhiteSpace(systemRoot)) return false;

                var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var allowedPaths = new[]
                {
                    Path.Combine(systemRoot, "Windows.old"),
                    Path.Combine(systemRoot, "$WINDOWS.~BT")
                };

                if (!allowedPaths.Any(allowed =>
                    string.Equals(fullPath, Path.GetFullPath(allowed).TrimEnd(Path.DirectorySeparatorChar),
                        StringComparison.OrdinalIgnoreCase)))
                {
                    return false;
                }

                // Ein Reparse Point könnte trotz korrektem Namen auf einen ganz
                // anderen Ordner zeigen und darf deshalb nie rekursiv gelöscht werden.
                return (File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) == 0;
            }
            catch (Exception ex)
            {
                Logger.LogError("Prüfung des geschützten Storage-Pfads", ex);
                return false;
            }
        }

        // Löscht rekursiv Datei für Datei, überspringt einzelne nicht
        // löschbare Dateien statt komplett abzubrechen.
        private static (int deleted, int failed) DeleteRecursiveBestEffort(string root)
        {
            int deleted = 0, failed = 0;

            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToList(); }
            catch { return (0, 0); }

            foreach (var file in files)
            {
                try
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                    deleted++;
                }
                catch
                {
                    failed++;
                }
            }

            // Leere Unterordner von innen nach außen entfernen (umgekehrte
            // Reihenfolge, damit tiefste Ordner zuerst dran sind).
            try
            {
                var dirs = Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                    .OrderByDescending(d => d.Length);

                foreach (var dir in dirs)
                {
                    try { Directory.Delete(dir); } // nur wenn leer
                    catch { /* nicht leer oder gesperrt - einfach liegen lassen */ }
                }
            }
            catch { /* Ordnerliste nicht zugreifbar - ignorieren */ }

            return (deleted, failed);
        }

        private static (bool success, string message) EmptyRecycleBin()
        {
            try
            {
                const uint SHERB_NOCONFIRMATION = 0x00000001;
                const uint SHERB_NOPROGRESSUI = 0x00000002;
                const uint SHERB_NOSOUND = 0x00000004;

                SHEmptyRecycleBin(IntPtr.Zero, null,
                    SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);

                return (true, "Papierkorb geleert.");
            }
            catch (Exception ex)
            {
                return (false, $"Fehler: {ex.Message}");
            }
        }

        private static (bool success, string message) RunHiddenCommand(string fileName, string arguments)
        {
            try
            {
                var p = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = arguments,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };

                p.Start();
                p.WaitForExit(15000);

                return p.ExitCode == 0
                    ? (true, "Erfolgreich ausgeführt.")
                    : (false, $"Beendet mit Code {p.ExitCode}.");
            }
            catch (Exception ex)
            {
                return (false, $"Fehler: {ex.Message}");
            }
        }

        // ================= HELPERS =================

        public static string FormatBytes(long bytes)
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

        // ================= NATIVE =================

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct SHQUERYRBINFO
        {
            public int cbSize;
            public long i64Size;
            public long i64NumItems;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern uint SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);
    }
}
