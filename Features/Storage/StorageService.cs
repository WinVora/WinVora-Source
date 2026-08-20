using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
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
            string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            bool en = Localization.CurrentLanguage == "en";

            return new List<StorageCategory>
            {
                new StorageCategory
                {
                    Key = "downloads",
                    Name = en ? "Downloads" : "Downloads",
                    Description = en ? "Files in your Downloads folder (personal files may be included)" : "Dateien im Downloads-Ordner (kann persönliche Dateien enthalten)",
                    Paths = new[] { downloads }
                },
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

        public static async Task<List<StorageCategory>> GetCategoriesWithSizesAsync(CancellationToken cancellationToken = default)
        {
            var categories = GetCategoryDefinitions();
            using var scanGate = new SemaphoreSlim(3, 3);
            var tasks = categories.Select(async c =>
            {
                await scanGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Run(() =>
                    {
                        c.SizeBytes = c.ActionType switch
                        {
                            StorageActionType.RecycleBin => GetRecycleBinSize(),
                            StorageActionType.DnsFlush => 0,
                            StorageActionType.StoreCacheReset => 0,
                            _ => c.Paths.Sum(p => GetDirectorySize(p, c.FilePattern, cancellationToken))
                        };

                        c.SizeDisplay = c.ActionType is StorageActionType.DnsFlush or StorageActionType.StoreCacheReset
                            ? Localization.T("Common.Action")
                            : FormatBytes(c.SizeBytes);
                    }, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    scanGate.Release();
                }
            });

            await Task.WhenAll(tasks);

            return categories;
        }

        public static async Task<(bool success, string message)> DeleteCategoryAsync(
            StorageCategory category, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (category.ActionType)
            {
                case StorageActionType.RecycleBin:
                    return await Task.Run(EmptyRecycleBin, cancellationToken).ConfigureAwait(false);

                case StorageActionType.DnsFlush:
                    return await RunHiddenCommandAsync(
                        Path.Combine(Environment.SystemDirectory, "ipconfig.exe"),
                        "/flushdns", TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false);

                case StorageActionType.StoreCacheReset:
                    return await RunHiddenCommandAsync(
                        Path.Combine(Environment.SystemDirectory, "wsreset.exe"),
                        string.Empty, TimeSpan.FromSeconds(60), cancellationToken).ConfigureAwait(false);

                default:
                    if (category.Key is "old_install_files" or "upgrade_logs")
                        return await DeleteProtectedFolderAsync(
                            category.Paths.FirstOrDefault() ?? string.Empty,
                            cancellationToken).ConfigureAwait(false);

                    return await Task.Run(() =>
                    {
                        int deleted = 0, failed = 0;
                        foreach (var path in category.Paths)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var (d, f) = DeleteDirectoryContents(path, category.FilePattern, cancellationToken);
                            deleted += d;
                            failed += f;
                        }

                        return failed == 0
                            ? (true, $"{deleted} Element(e) gelöscht.")
                            : (deleted > 0,
                                $"{deleted} Element(e) gelöscht, {failed} übersprungen (in Benutzung oder keine Berechtigung).");
                    }, cancellationToken).ConfigureAwait(false);
            }
        }

        // ================= SIZE CALCULATION =================

        private static long GetDirectorySize(string path, string pattern, CancellationToken cancellationToken)
        {
            if (!SystemAccess.FileSystem.DirectoryExists(path) || IsReparsePoint(path)) return 0;

            long size = 0;
            var pending = new Stack<string>();
            pending.Push(path);
            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string current = pending.Pop();
                try
                {
                    foreach (var file in SystemAccess.FileSystem.EnumerateFiles(current, pattern))
                    {
                        try { size += new FileInfo(file).Length; }
                        catch { /* Datei nicht zugreifbar - ignorieren */ }
                    }
                    foreach (var directory in SystemAccess.FileSystem.EnumerateDirectories(current))
                        if (!IsReparsePoint(directory)) pending.Push(directory);
                }
                catch { /* Ordner nicht zugreifbar - ignorieren */ }
            }

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

        private static (int deleted, int failed) DeleteDirectoryContents(
            string path, string pattern, CancellationToken cancellationToken)
        {
            int deleted = 0, failed = 0;
            if (!Directory.Exists(path)) return (0, 0);

            if (pattern != "*")
            {
                foreach (var file in SafeEnumerateFiles(path, pattern))
                {
                    cancellationToken.ThrowIfCancellationRequested();
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
                cancellationToken.ThrowIfCancellationRequested();
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
                    cancellationToken.ThrowIfCancellationRequested();
                    if (IsReparsePoint(dir))
                    {
                        failed++;
                        continue;
                    }
                    try
                    {
                        var (nestedDeleted, nestedFailed) = DeleteRecursiveBestEffort(dir, cancellationToken);
                        deleted += nestedDeleted;
                        failed += nestedFailed;
                        Directory.Delete(dir);
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

        internal static bool IsReparsePoint(string path)
        {
            try { return (SystemAccess.FileSystem.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
            catch { return true; }
        }

        // Für Ordner, die (wie Windows.old) TrustedInstaller gehören:
        // erst Besitz übernehmen, dann Vollzugriff für Administratoren
        // setzen, erst dann löschen. Auch dann kann es ohne aktive
        // Admin-Rechte fehlschlagen - das wird sauber zurückgemeldet.
        private static async Task<(bool success, string message)> DeleteProtectedFolderAsync(
            string path,
            CancellationToken cancellationToken)
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
                string systemDirectory = Environment.SystemDirectory;
                var ownership = await RunHiddenCommandAsync(
                    Path.Combine(systemDirectory, "takeown.exe"),
                    $"/F \"{path}\" /R /D Y",
                    TimeSpan.FromMinutes(5),
                    cancellationToken).ConfigureAwait(false);
                if (!ownership.success)
                    return (false, $"Besitzübernahme fehlgeschlagen: {ownership.message}");

                // S-1-5-32-544 = lokale Gruppe "Administratoren" (sprachunabhängig)
                var permissions = await RunHiddenCommandAsync(
                    Path.Combine(systemDirectory, "icacls.exe"),
                    $"\"{path}\" /grant *S-1-5-32-544:F /T /C /Q",
                    TimeSpan.FromMinutes(5),
                    cancellationToken).ConfigureAwait(false);
                if (!permissions.success)
                    return (false, $"Berechtigungen konnten nicht gesetzt werden: {permissions.message}");

                cancellationToken.ThrowIfCancellationRequested();

                // BUGFIX: Directory.Delete(path, true) ist alles-oder-nichts -
                // eine einzige besonders geschützte Datei (z.B. "bcd", die
                // Windows fürs Rückgängig-Machen des Updates braucht) lässt
                // sich selbst mit Admin-Rechten nicht löschen und ließ dadurch
                // bisher ALLES scheitern. Jetzt wird Datei für Datei einzeln
                // versucht, geschützte Einzeldateien werden einfach übersprungen.
                var (deleted, failed) = DeleteRecursiveBestEffort(path, cancellationToken);

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
                var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!IsProtectedFolderPathAllowlisted(fullPath)) return false;

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
        private static (int deleted, int failed) DeleteRecursiveBestEffort(
            string root, CancellationToken cancellationToken = default)
        {
            int deleted = 0, failed = 0;
            if (IsReparsePoint(root)) return (0, 1);
            var pending = new Stack<string>();
            var directories = new List<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string current = pending.Pop();
                try
                {
                    foreach (var file in Directory.EnumerateFiles(current, "*", SearchOption.TopDirectoryOnly))
                    {
                        try
                        {
                            File.SetAttributes(file, FileAttributes.Normal);
                            File.Delete(file);
                            deleted++;
                        }
                        catch { failed++; }
                    }
                    foreach (var directory in Directory.EnumerateDirectories(current, "*", SearchOption.TopDirectoryOnly))
                    {
                        if (IsReparsePoint(directory))
                        {
                            failed++;
                            continue;
                        }
                        directories.Add(directory);
                        pending.Push(directory);
                    }
                }
                catch { failed++; }
            }

            // Leere Unterordner von innen nach außen entfernen (umgekehrte
            // Reihenfolge, damit tiefste Ordner zuerst dran sind).
            foreach (var dir in directories.OrderByDescending(d => d.Length))
            {
                try { Directory.Delete(dir); } // nur wenn leer
                catch { /* nicht leer oder gesperrt - einfach liegen lassen */ }
            }

            return (deleted, failed);
        }

        private static (bool success, string message) EmptyRecycleBin()
        {
            try
            {
                const uint SHERB_NOCONFIRMATION = 0x00000001;
                const uint SHERB_NOPROGRESSUI = 0x00000002;
                const uint SHERB_NOSOUND = 0x00000004;

                uint result = SHEmptyRecycleBin(IntPtr.Zero, null,
                    SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);

                return result == 0
                    ? (true, "Papierkorb geleert.")
                    : (false, $"Papierkorb konnte nicht geleert werden (HRESULT 0x{result:X8}).");
            }
            catch (Exception ex)
            {
                return (false, $"Fehler: {ex.Message}");
            }
        }

        private static async Task<(bool success, string message)> RunHiddenCommandAsync(
            string fileName,
            string arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            try
            {
                var result = await SystemAccess.ProcessRunner.RunAsync(
                    new ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = arguments
                    },
                    timeout,
                    cancellationToken).ConfigureAwait(false);

                if (result.TimedOut)
                    return (false, $"Zeitüberschreitung nach {timeout.TotalSeconds:0} Sekunden.");

                string details = string.IsNullOrWhiteSpace(result.StandardError)
                    ? result.StandardOutput.Trim()
                    : result.StandardError.Trim();
                return result.ExitCode == 0
                    ? (true, "Erfolgreich ausgeführt.")
                    : (false, string.IsNullOrWhiteSpace(details)
                        ? $"Beendet mit Code {result.ExitCode}."
                        : $"Beendet mit Code {result.ExitCode}: {details}");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Logger.LogError($"Systembefehl ausführen: {Path.GetFileName(fileName)}", ex);
                return (false, $"Fehler: {ex.Message}");
            }
        }

        internal static bool IsProtectedFolderPathAllowlisted(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            try
            {
                string windowsPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                string? systemRoot = Path.GetPathRoot(windowsPath);
                if (string.IsNullOrWhiteSpace(systemRoot)) return false;

                string fullPath = Path.GetFullPath(path).TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
                return new[]
                {
                    Path.Combine(systemRoot, "Windows.old"),
                    Path.Combine(systemRoot, "$WINDOWS.~BT")
                }.Any(allowed => string.Equals(
                    fullPath,
                    Path.GetFullPath(allowed).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
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

        [DllImport("shell32.dll", EntryPoint = "SHQueryRecycleBinW", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern uint SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);
    }
}
