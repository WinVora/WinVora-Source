using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using Microsoft.Win32;

namespace WinVora
{
    public enum SystemInfoSection { Device, OperatingSystem, Cpu, Ram, Board, Security, Gpu, Drives, Network, Battery }

    [SupportedOSPlatform("windows")]
    public static partial class SystemInfoProvider
    {
        private static PerformanceCounter? _cpuCounter;
        private static readonly SystemInfoSnapshot CachedSnapshot = new();
        private static readonly ConcurrentDictionary<SystemInfoSection, DateTime> SectionCacheUtc = new();
        private static readonly ConcurrentDictionary<SystemInfoSection, SemaphoreSlim> SectionGates = new();
        private static readonly TimeSpan SectionCacheLifetime = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan SectionTimeout = TimeSpan.FromSeconds(8);

        public static Task<(string Antivirus, string Firewall)> GetFastSecurityStatusAsync(CancellationToken cancellationToken = default)
        {
            return RunMeasuredAsync("Sicherheit (Schnellprüfung)", () => Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool en = Localization.CurrentLanguage == "en";
                string antivirus = en ? "Unknown" : "Unbekannt";
                string firewall = en ? "Unknown" : "Unbekannt";

                try
                {
                    antivirus = ReadAntivirusStatus(en);
                }
                catch (Exception ex) { Logger.LogErrorOnce("Schnellprüfung Antivirus", ex); }

                try
                {
                    string[] profiles = { "DomainProfile", "PublicProfile", "StandardProfile" };
                    bool enabled = profiles.Any(profile =>
                    {
                        using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\{profile}");
                        return Convert.ToInt32(key?.GetValue("EnableFirewall", 0)) == 1;
                    });
                    firewall = enabled ? (en ? "Active" : "Aktiv") : (en ? "Disabled" : "Deaktiviert");
                }
                catch (Exception ex) { Logger.LogErrorOnce("Schnellprüfung Firewall", ex); }

                return (antivirus, firewall);
            }, cancellationToken), cancellationToken);
        }

        // ================= FULL SNAPSHOT =================
        public static async Task<SystemInfoSnapshot> GetFullSnapshotAsync(CancellationToken cancellationToken = default)
        {
            FillBasic(CachedSnapshot);
            // Die erweiterte Sicherheitsabfrage kann auf manchen PCs wegen
            // TPM/BitLocker-WMI rund fünf Sekunden dauern. Für das Dashboard
            // läuft bereits die schnelle, unabhängige Sicherheitsprüfung.
            // Detailwerte werden deshalb erst beim Öffnen der Kategorie geladen.
            var sections = Enum.GetValues<SystemInfoSection>()
                .Where(section => section != SystemInfoSection.Security);
            await Task.WhenAll(sections.Select(section => RefreshSectionCoreAsync(
                CachedSnapshot, section, force: false, cancellationToken)));
            return CachedSnapshot.Clone();
        }

        public static async Task RefreshSectionAsync(SystemInfoSnapshot snapshot, SystemInfoSection section,
            CancellationToken cancellationToken = default)
        {
            await RefreshSectionCoreAsync(CachedSnapshot, section, force: true, cancellationToken);
            snapshot.CopySectionFrom(CachedSnapshot, section);
        }

        private static async Task RefreshSectionCoreAsync(SystemInfoSnapshot snapshot, SystemInfoSection section,
            bool force, CancellationToken cancellationToken)
        {
            if (!force && SectionCacheUtc.TryGetValue(section, out var cachedAt) &&
                DateTime.UtcNow - cachedAt < SectionCacheLifetime)
                return;

            var sectionGate = SectionGates.GetOrAdd(section, _ => new SemaphoreSlim(1, 1));
            await sectionGate.WaitAsync(cancellationToken);
            try
            {
                if (!force && SectionCacheUtc.TryGetValue(section, out cachedAt) &&
                    DateTime.UtcNow - cachedAt < SectionCacheLifetime) return;

                try
                {
                    await RunMeasuredAsync($"Systeminfo {section}", () => Task.Run(() =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        switch (section)
                        {
                            case SystemInfoSection.Device: FillBasic(snapshot); FillSystemInfo(snapshot); break;
                            case SystemInfoSection.OperatingSystem: FillOS(snapshot); FillLastUpdate(snapshot); FillDirectX(snapshot); break;
                            case SystemInfoSection.Cpu: FillCpu(snapshot); break;
                            case SystemInfoSection.Ram: FillRam(snapshot); break;
                            case SystemInfoSection.Board: FillBoardAndBios(snapshot); break;
                            case SystemInfoSection.Security: FillSecurity(snapshot); break;
                            case SystemInfoSection.Gpu: FillGpu(snapshot); break;
                            case SystemInfoSection.Drives: FillDrives(snapshot); break;
                            case SystemInfoSection.Network: FillNetwork(snapshot); break;
                            case SystemInfoSection.Battery: FillBattery(snapshot); break;
                        }
                    }, cancellationToken), cancellationToken);
                    SectionCacheUtc[section] = DateTime.UtcNow;
                }
                catch (TimeoutException)
                {
                    // Ein langsamer WMI-Bereich darf die übrigen, bereits
                    // verfügbaren Systeminformationen nicht verwerfen.
                }
            }
            finally { sectionGate.Release(); }
        }

        private static async Task<T> RunMeasuredAsync<T>(string name, Func<Task<T>> operation, CancellationToken token)
        {
            var timer = Stopwatch.StartNew();
            try { return await operation().WaitAsync(SectionTimeout, token); }
            catch (TimeoutException ex) { Logger.LogError($"{name} (Timeout nach {SectionTimeout.TotalSeconds:0}s)", ex); throw; }
            finally
            {
                if (timer.ElapsedMilliseconds >= 750)
                    Logger.Log($"Langsame Abfrage '{name}': {timer.ElapsedMilliseconds} ms");
            }
        }

        private static async Task RunMeasuredAsync(string name, Func<Task> operation, CancellationToken token)
        {
            await RunMeasuredAsync(name, async () => { await operation(); return true; }, token);
        }

        private static ManagementObjectSearcher CreateWmiSearcher(string query) =>
            CreateWmiSearcher(@"root\CIMV2", query);

        private static ManagementObjectSearcher CreateWmiSearcher(string scope, string query)
        {
            var searcher = new ManagementObjectSearcher(scope, query);
            searcher.Options.Timeout = TimeSpan.FromSeconds(5);
            searcher.Options.ReturnImmediately = true;
            return searcher;
        }

        // Initialisiert den CPU-Performance-Counter frühzeitig, im Hintergrund
        // beim App-Start. PerformanceCounter braucht zwei Messungen mit etwas
        // zeitlichem Abstand dazwischen, um einen sinnvollen Prozentwert zu
        // liefern - ruft man ihn ganz frisch initialisiert direkt zweimal
        // hintereinander auf, kommt oft ein falscher/niedriger Wert raus.
        // Wird dieser Aufruf hier früh (parallel zum restlichen Laden)
        // gemacht, ist beim ersten echten Live-Update schon genug Zeit
        // vergangen, damit der Wert von Anfang an stimmt.
        public static void WarmUpCpuCounter()
        {
            try
            {
                if (_cpuCounter == null)
                {
                    _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                    _cpuCounter.NextValue();
                }
            }
            catch (Exception ex) { Logger.LogErrorOnce("CPU-Leistungszähler initialisieren", ex); }
        }

        // ================= LIVE USAGE =================
        public static (double cpu, double ram, double gpu, double ramUsedGb, double ramTotalGb) GetLiveUsage()
        {
            double cpu = 0;
            double ram = 0;
            double ramUsedGb = 0;
            double ramTotalGb = 0;

            try
            {
                if (_cpuCounter == null)
                {
                    _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                    _cpuCounter.NextValue();
                }

                cpu = Math.Round(_cpuCounter.NextValue(), 1);
            }
            catch (Exception ex) { Logger.LogErrorOnce("CPU-Liveauslastung lesen", ex); }

            try
            {
                var mem = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(mem))
                {
                    ram = Math.Round((double)mem.dwMemoryLoad, 1);
                    ramTotalGb = Math.Round(mem.ullTotalPhys / 1024d / 1024 / 1024, 1);
                    ramUsedGb = Math.Round(ramTotalGb - (mem.ullAvailPhys / 1024d / 1024 / 1024), 1);
                }
            }
            catch (Exception ex) { Logger.LogErrorOnce("RAM-Liveauslastung lesen", ex); }

            return (cpu, ram, 0, ramUsedGb, ramTotalGb);
        }

        // ================= BASIC =================
        private static void Safe(Action a, [System.Runtime.CompilerServices.CallerMemberName] string context = "Systeminfo")
        {
            try { a(); }
            catch (Exception ex) { Logger.LogErrorOnce(context, ex); }
        }

    }
}
