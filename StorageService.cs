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

            return new List<StorageCategory>
            {
                new StorageCategory
                {
                    Key = "user_temp",
                    Name = "Benutzer Temp",
                    Description = "Temporäre Dateien des aktuellen Benutzerkontos",
                    Paths = new[] { temp }
                },
                new StorageCategory
                {
                    Key = "windows_temp",
                    Name = "Windows Temp",
                    Description = "Temporäre Systemdateien von Windows",
                    Paths = new[] { Path.Combine(windows, "Temp") },
                    RequiresAdmin = true
                },
                new StorageCategory
                {
                    Key = "prefetch",
                    Name = "Prefetch",
                    Description = "Zwischengespeicherte Startdaten für schnelleren Programmstart",
                    Paths = new[] { Path.Combine(windows, "Prefetch") },
                    RequiresAdmin = true
                },
                new StorageCategory
                {
                    Key = "recycle_bin",
                    Name = "Papierkorb",
                    Description = "Endgültig gelöschte Dateien aus dem Papierkorb entfernen",
                    ActionType = StorageActionType.RecycleBin
                },
                new StorageCategory
                {
                    Key = "dx_shader_cache",
                    Name = "DirectX Shader Cache",
                    Description = "Zwischengespeicherte Grafik-Shader",
                    Paths = new[] { Path.Combine(localAppData, "D3DSCache") }
                },
                new StorageCategory
                {
                    Key = "update_cache",
                    Name = "Windows Update Cache",
                    Description = "Heruntergeladene Update-Dateien (inkl. alter, nicht mehr benötigter Downloads)",
                    Paths = new[] { Path.Combine(windows, "SoftwareDistribution", "Download") },
                    RequiresAdmin = true
                },
                new StorageCategory
                {
                    Key = "delivery_optimization",
                    Name = "Delivery Optimization Dateien",
                    Description = "Zwischengespeicherte Update-Teile für die Peer-Verteilung",
                    Paths = new[] { Path.Combine(programData, "Microsoft", "Network", "Downloader") },
                    RequiresAdmin = true
                },
                new StorageCategory
                {
                    Key = "wer",
                    Name = "Windows Error Reporting",
                    Description = "Absturzberichte, die an Microsoft gesendet oder lokal gespeichert wurden",
                    Paths = new[] { Path.Combine(programData, "Microsoft", "Windows", "WER") },
                    RequiresAdmin = true
                },
                new StorageCategory
                {
                    Key = "minidump",
                    Name = "MiniDump Dateien",
                    Description = "Kleine Absturz-Speicherabbilder von Systemabstürzen (Bluescreens)",
                    Paths = new[] { Path.Combine(windows, "Minidump") },
                    RequiresAdmin = true
                },
                new StorageCategory
                {
                    Key = "crash_dumps",
                    Name = "Crash Dumps",
                    Description = "Vollständige Absturz-Speicherabbilder von Anwendungen",
                    Paths = new[] { Path.Combine(localAppData, "CrashDumps") }
                },
                new StorageCategory
                {
                    Key = "thumbnail_cache",
                    Name = "Thumbnail Cache",
                    Description = "Zwischengespeicherte Miniaturansichten des Explorers",
                    Paths = new[] { Path.Combine(localAppData, "Microsoft", "Windows", "Explorer") },
                    FilePattern = "thumbcache_*.db"
                },
                new StorageCategory
                {
                    Key = "browser_cache",
                    Name = "Browser Cache",
                    Description = "Zwischenspeicher von Chrome und Edge",
                    Paths = new[]
                    {
                        Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default", "Cache"),
                        Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default", "Cache")
                    }
                },
                new StorageCategory
                {
                    Key = "logs",
                    Name = "Logs",
                    Description = "Allgemeine Windows-Protokolldateien",
                    Paths = new[] { Path.Combine(windows, "Logs") },
                    RequiresAdmin = true
                },
                new StorageCategory
                {
                    Key = "setup_logs",
                    Name = "Setup Logs",
                    Description = "Protokolle von Windows-Setup und Feature-Updates",
                    Paths = new[] { Path.Combine(windows, "Panther") },
                    RequiresAdmin = true
                },
                new StorageCategory
                {
                    Key = "defender_temp",
                    Name = "Defender temporäre Dateien",
                    Description = "Temporäre Support- und Protokolldateien von Windows Defender",
                    Paths = new[] { Path.Combine(programData, "Microsoft", "Windows Defender", "Support") },
                    RequiresAdmin = true
                },
                new StorageCategory
                {
                    Key = "inet_cache",
                    Name = "Temporary Internet Files",
                    Description = "Zwischengespeicherte Webinhalte des Internet Explorer / WebView",
                    Paths = new[] { Path.Combine(localAppData, "Microsoft", "Windows", "INetCache") }
                },
                new StorageCategory
                {
                    Key = "store_cache",
                    Name = "Microsoft Store Cache",
                    Description = "Setzt den Zwischenspeicher des Microsoft Store zurück",
                    ActionType = StorageActionType.StoreCacheReset
                },
                new StorageCategory
                {
                    Key = "dns_cache",
                    Name = "DNS Cache",
                    Description = "Zwischengespeicherte DNS-Auflösungen",
                    ActionType = StorageActionType.DnsFlush
                },
                new StorageCategory
                {
                    Key = "upgrade_logs",
                    Name = "Windows Upgrade Logs",
                    Description = "Protokolle eines vorherigen Windows-Feature-Updates",
                    Paths = new[] { Path.Combine(systemDrive, "$WINDOWS.~BT") },
                    RequiresAdmin = true
                },
                new StorageCategory
                {
                    Key = "old_install_files",
                    Name = "Nicht mehr benötigte Installationsdateien",
                    Description = "Alte Windows-Installation nach einem Upgrade (Windows.old) - benötigt Admin-Rechte, ggf. nicht vollständig löschbar",
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
                        // BUGFIX: Windows.old gehört TrustedInstaller - normales
                        // File.Delete/Directory.Delete schlägt dort so gut wie
                        // immer fehl, selbst mit Admin-Rechten. Wir übernehmen
                        // vorher gezielt den Besitz und setzen die Rechte.
                        if (category.Key == "old_install_files")
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

            try
            {
                RunHiddenCommand("takeown.exe", $"/F \"{path}\" /R /D Y");
                // S-1-5-32-544 = lokale Gruppe "Administratoren" (sprachunabhängig)
                RunHiddenCommand("icacls.exe", $"\"{path}\" /grant *S-1-5-32-544:F /T /C /Q");

                Directory.Delete(path, true);
                return (true, "Windows.old wurde entfernt.");
            }
            catch (Exception ex)
            {
                return (false,
                    $"Konnte nicht vollständig entfernt werden (Admin-Rechte erforderlich): {ex.Message}");
            }
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