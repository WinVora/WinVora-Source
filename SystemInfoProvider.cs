using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace WinVora
{
    [SupportedOSPlatform("windows")]
    public static class SystemInfoProvider
    {
        private static PerformanceCounter? _cpuCounter;

        // ================= FULL SNAPSHOT =================
        public static async Task<SystemInfoSnapshot> GetFullSnapshotAsync()
        {
            var s = new SystemInfoSnapshot();

            FillBasic(s);

            var tasks = new[]
            {
                Task.Run(() => FillSystemInfo(s)),
                Task.Run(() => FillOS(s)),
                Task.Run(() => FillLastUpdate(s)),
                Task.Run(() => FillCpu(s)),
                Task.Run(() => FillRam(s)),
                Task.Run(() => FillGpu(s)),
                Task.Run(() => FillDrives(s)),
                Task.Run(() => FillNetwork(s)),
                Task.Run(() => FillSecurity(s)),
                Task.Run(() => FillBoardAndBios(s)),
                Task.Run(() => FillBattery(s)),
                Task.Run(() => FillDirectX(s)),
            };

            await Task.WhenAll(tasks);

            return s;
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
            catch { }
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
            catch { }

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
            catch { }

            return (cpu, ram, 0, ramUsedGb, ramTotalGb);
        }

        // ================= BASIC =================
        private static void FillBasic(SystemInfoSnapshot s)
        {
            Safe(() =>
            {
                s.ComputerName = Environment.MachineName;
                s.UserName = Environment.UserName;
                s.Architecture = RuntimeInformation.OSArchitecture.ToString();
                s.DotNetVersion = RuntimeInformation.FrameworkDescription;
            });
        }

        // ================= SYSTEM =================
        private static void FillSystemInfo(SystemInfoSnapshot s)
        {
            bool en = Localization.CurrentLanguage == "en";
            Safe(() =>
            {
                using var cs = new ManagementObjectSearcher("SELECT Manufacturer, Model, HypervisorPresent FROM Win32_ComputerSystem");

                foreach (ManagementObject mo in cs.Get())
                {
                    s.Manufacturer = mo["Manufacturer"]?.ToString() ?? "N/A";
                    s.Model = mo["Model"]?.ToString() ?? "N/A";

                    var hyperV = mo["HypervisorPresent"];
                    if (hyperV != null)
                        s.Virtualization = (bool)hyperV ? (en ? "Active" : "Aktiv") : (en ? "Available, not active" : "Verfügbar, nicht aktiv");

                    break;
                }
            });
        }

        // ================= OS =================
        private static void FillOS(SystemInfoSnapshot s)
        {
            bool en = Localization.CurrentLanguage == "en";
            Safe(() =>
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_OperatingSystem");

                foreach (ManagementObject mo in searcher.Get())
                {
                    s.WindowsEdition = mo["Caption"]?.ToString() ?? "N/A";
                    s.WindowsVersion = mo["Version"]?.ToString() ?? "N/A";
                    s.BuildNumber = mo["BuildNumber"]?.ToString() ?? "N/A";

                    if (mo["InstallDate"] != null)
                        s.InstallDate = ManagementDateTimeConverter.ToDateTime(mo["InstallDate"].ToString())
                            .ToString("yyyy-MM-dd");

                    if (mo["LastBootUpTime"] != null)
                    {
                        var dt = ManagementDateTimeConverter.ToDateTime(mo["LastBootUpTime"].ToString());
                        var up = DateTime.Now - dt;
                        s.Uptime = $"{up.Days}d {up.Hours}h {up.Minutes}m";
                    }

                    s.ActivationStatus = IsActivated() ? (en ? "Activated" : "Aktiviert") : (en ? "Not activated" : "Nicht aktiviert");
                    break;
                }
            });
        }

        // ================= LAST WINDOWS UPDATE =================
        // BUGFIX: Dieses Feld wurde vorher nirgends befüllt, obwohl die UI
        // (SysLastUpdate) es anzeigt -> stand immer auf "N/A". Wir ermitteln
        // jetzt das jüngste installierte Hotfix-Datum über Win32_QuickFixEngineering.
        private static void FillLastUpdate(SystemInfoSnapshot s)
        {
            Safe(() =>
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT HotFixID, InstalledOn FROM Win32_QuickFixEngineering");

                DateTime? latest = null;

                foreach (ManagementObject mo in searcher.Get())
                {
                    var installedOnRaw = mo["InstalledOn"]?.ToString();
                    if (string.IsNullOrWhiteSpace(installedOnRaw)) continue;

                    // InstalledOn kommt von WMI meist bereits als lokalisiertes
                    // Datum (kein CIM_DATETIME) - daher normales DateTime.TryParse.
                    if (DateTime.TryParse(
                            installedOnRaw,
                            System.Globalization.CultureInfo.CurrentCulture,
                            System.Globalization.DateTimeStyles.None,
                            out var dt))
                    {
                        if (latest == null || dt > latest.Value)
                            latest = dt;
                    }
                }

                s.LastUpdate = latest.HasValue ? latest.Value.ToString("yyyy-MM-dd") : "N/A";
            });
        }

        // ================= CPU =================
        private static void FillCpu(SystemInfoSnapshot s)
        {
            Safe(() =>
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor");

                foreach (ManagementObject mo in searcher.Get())
                {
                    s.CpuName = mo["Name"]?.ToString() ?? "N/A";
                    s.CpuCores = mo["NumberOfCores"]?.ToString() ?? "N/A";
                    s.CpuThreads = mo["NumberOfLogicalProcessors"]?.ToString() ?? "N/A";

                    s.CpuClock = mo["MaxClockSpeed"] != null
                        ? $"{Convert.ToDouble(mo["MaxClockSpeed"]) / 1000:0.00} GHz"
                        : "N/A";

                    break;
                }
            });
        }

        // ================= RAM =================
        private static void FillRam(SystemInfoSnapshot s)
        {
            Safe(() =>
            {
                var mem = new MEMORYSTATUSEX();

                if (GlobalMemoryStatusEx(mem))
                {
                    double total = mem.ullTotalPhys / 1024d / 1024 / 1024;
                    double free = mem.ullAvailPhys / 1024d / 1024 / 1024;
                    double used = total - free;

                    s.RamTotal = $"{total:0.0} GB";
                    s.RamFree = $"{free:0.0} GB";
                    s.RamUsed = $"{used:0.0} GB";
                }
            });
        }

        // ================= GPU =================
        private static void FillGpu(SystemInfoSnapshot s)
        {
            Safe(() =>
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");

                s.Gpus = searcher.Get()
                    .Cast<ManagementObject>()
                    .Select(m => m["Name"]?.ToString() ?? "Unknown GPU")
                    .ToArray();
            });
        }

        // ================= DRIVES =================
        private static void FillDrives(SystemInfoSnapshot s)
        {
            Safe(() =>
            {
                s.Drives = DriveInfo.GetDrives()
                    .Where(d => d.IsReady)
                    .Select(d => new DriveSummary
                    {
                        Name = d.Name,
                        TotalSize = $"{d.TotalSize / 1024d / 1024 / 1024:0.0} GB",
                        FreeSpace = $"{d.TotalFreeSpace / 1024d / 1024 / 1024:0.0} GB"
                    }).ToArray();
            });
        }

        // ================= NETWORK =================
        private static void FillNetwork(SystemInfoSnapshot s)
        {
            Safe(() =>
            {
                s.NetworkAdapters = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                                n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .Select(n =>
                    {
                        var ip = n.GetIPProperties();

                        var ipv6 = ip.UnicastAddresses.FirstOrDefault(x =>
                                x.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)?
                                .Address.ToString() ?? "N/A";

                        return new NetworkSummary
                        {
                            Name = n.Name,
                            IPv4 = ip.UnicastAddresses.FirstOrDefault(x =>
                                x.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?
                                .Address.ToString() ?? "N/A",
                            IPv6 = ipv6,
                            MacAddress = FormatMacAddress(n.GetPhysicalAddress()),
                            Gateway = ip.GatewayAddresses.FirstOrDefault()?.Address.ToString() ?? "N/A",
                            Dns = string.Join(", ", ip.DnsAddresses)
                        };
                    }).ToArray();
            });
        }

        // BUGFIX: Vorher lieferte GetPhysicalAddress().ToString() eine
        // unleserliche Zeichenkette ohne Trennzeichen (z.B. "001A2B3C4D5E").
        // Jetzt im gewohnten "AA:BB:CC:DD:EE:FF"-Format.
        private static string FormatMacAddress(System.Net.NetworkInformation.PhysicalAddress address)
        {
            var bytes = address.GetAddressBytes();
            return bytes.Length == 0
                ? "N/A"
                : string.Join(":", bytes.Select(b => b.ToString("X2")));
        }

        // ================= SECURITY =================
        private static void FillSecurity(SystemInfoSnapshot s)
        {
            bool en = Localization.CurrentLanguage == "en";

            // Secure Boot
            try
            {
                using var rk = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
                var val = rk?.GetValue("UEFISecureBootEnabled")?.ToString();
                s.SecureBoot = val == "1" ? (en ? "Enabled" : "Aktiviert") : (val == "0" ? (en ? "Disabled" : "Deaktiviert") : (en ? "Not available" : "Nicht verfügbar"));
            }
            catch
            {
                s.SecureBoot = en ? "Not available" : "Nicht verfügbar";
            }

            // TPM
            try
            {
                using var tpm = new ManagementObjectSearcher(@"root\CIMV2\Security\MicrosoftTpm", "SELECT SpecVersion FROM Win32_Tpm");
                s.TpmVersion = en ? "No TPM detected" : "Kein TPM erkannt";

                foreach (ManagementObject mo in tpm.Get())
                {
                    s.TpmVersion = mo["SpecVersion"]?.ToString() ?? (en ? "TPM present" : "TPM vorhanden");
                    break;
                }
            }
            catch
            {
                s.TpmVersion = en ? "No TPM detected" : "Kein TPM erkannt";
            }

            // Windows Defender
            try
            {
                using var defender = new ManagementObjectSearcher(@"root\Microsoft\Windows\Defender",
                    "SELECT AntivirusEnabled, RealTimeProtectionEnabled FROM MSFT_MpComputerStatus");
                s.DefenderStatus = en ? "Unknown" : "Unbekannt";

                foreach (ManagementObject mo in defender.Get())
                {
                    var av = Convert.ToBoolean(mo["AntivirusEnabled"]);
                    var rt = Convert.ToBoolean(mo["RealTimeProtectionEnabled"]);
                    s.DefenderStatus = av && rt ? (en ? "Active" : "Aktiv") : (en ? "Partial/Inactive" : "Teilweise/Inaktiv");
                    break;
                }
            }
            catch
            {
                s.DefenderStatus = en ? "Unknown" : "Unbekannt";
            }

            // Firewall
            try
            {
                using var fw = new ManagementObjectSearcher(@"root\StandardCimv2", "SELECT Enabled FROM MSFT_NetFirewallProfile");
                bool anyEnabled = false;

                foreach (ManagementObject mo in fw.Get())
                {
                    if (Convert.ToBoolean(mo["Enabled"])) anyEnabled = true;
                }

                s.FirewallStatus = anyEnabled ? (en ? "Active" : "Aktiv") : (en ? "Disabled" : "Deaktiviert");
            }
            catch
            {
                s.FirewallStatus = en ? "Unknown" : "Unbekannt";
            }

            // BitLocker (Laufwerk C:)
            try
            {
                using var bl = new ManagementObjectSearcher(@"root\CIMV2\Security\MicrosoftVolumeEncryption",
                    "SELECT ProtectionStatus FROM Win32_EncryptableVolume WHERE DriveLetter = 'C:'");
                s.BitLockerStatus = en ? "Not available" : "Nicht verfügbar";

                foreach (ManagementObject mo in bl.Get())
                {
                    var status = Convert.ToInt32(mo["ProtectionStatus"]);
                    s.BitLockerStatus = status switch
                    {
                        0 => en ? "Disabled" : "Deaktiviert",
                        1 => en ? "Enabled" : "Aktiviert",
                        2 => en ? "Unknown" : "Unbekannt",
                        _ => "N/A"
                    };
                    break;
                }
            }
            catch
            {
                // Ohne Admin-Rechte oft nicht abfragbar
                s.BitLockerStatus = en ? "Not available (admin rights may be required)" : "Nicht verfügbar (ggf. Adminrechte nötig)";
            }
        }

        // ================= BOARD + BIOS + SERIAL =================
        private static void FillBoardAndBios(SystemInfoSnapshot s)
        {
            Safe(() =>
            {
                using var board = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard");

                foreach (ManagementObject mo in board.Get())
                {
                    s.Mainboard = $"{mo["Manufacturer"]} {mo["Product"]}";
                    break;
                }

                using var bios = new ManagementObjectSearcher("SELECT SerialNumber, SMBIOSBIOSVersion FROM Win32_BIOS");

                foreach (ManagementObject mo in bios.Get())
                {
                    var serial = mo["SerialNumber"]?.ToString();

                    s.SerialNumber = string.IsNullOrWhiteSpace(serial) || serial.Contains("System")
                        ? (Localization.CurrentLanguage == "en" ? "Not available" : "Nicht verfügbar")
                        : serial;

                    s.BiosVersion = mo["SMBIOSBIOSVersion"]?.ToString() ?? "N/A";
                    break;
                }
            });
        }

        // ================= BATTERY =================
        private static void FillBattery(SystemInfoSnapshot s)
        {
            bool en = Localization.CurrentLanguage == "en";
            Safe(() =>
            {
                using var searcher = new ManagementObjectSearcher("SELECT EstimatedChargeRemaining, BatteryStatus FROM Win32_Battery");

                s.BatteryStatus = en ? "No battery detected" : "Kein Akku erkannt";

                foreach (ManagementObject mo in searcher.Get())
                {
                    var charge = mo["EstimatedChargeRemaining"]?.ToString() ?? "N/A";
                    var statusCode = mo["BatteryStatus"]?.ToString();
                    var charging = statusCode == "6" || statusCode == "7" || statusCode == "8";
                    s.BatteryStatus = $"{charge}% {(charging ? (en ? "(charging)" : "(lädt)") : "")}".Trim();
                    break;
                }
            });
        }

        // ================= DIRECTX =================
        // BUGFIX: Vorher wurde hart "DirectX 12" zurückgegeben, ganz ohne
        // irgendetwas zu prüfen. Jetzt wird die tatsächlich von der GPU/dem
        // Treiber unterstützte Direct3D-Feature-Stufe per D3D11CreateDevice
        // ermittelt (funktioniert auch für D3D12-fähige Hardware, da die
        // Feature-Level-Abfrage über D3D11 abwärtskompatibel ist).
        private static void FillDirectX(SystemInfoSnapshot s)
        {
            s.DirectXVersion = GetDirectXFeatureLevel();
        }

        private static string GetDirectXFeatureLevel()
        {
            IntPtr device = IntPtr.Zero;
            IntPtr context = IntPtr.Zero;

            try
            {
                var featureLevels = new[]
                {
                    D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_12_2,
                    D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_12_1,
                    D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_12_0,
                    D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_1,
                    D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_0,
                };

                int hr = D3D11CreateDevice(
                    IntPtr.Zero,
                    D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_HARDWARE,
                    IntPtr.Zero,
                    0,
                    featureLevels,
                    (uint)featureLevels.Length,
                    D3D11_SDK_VERSION,
                    out device,
                    out D3D_FEATURE_LEVEL achieved,
                    out context);

                if (hr == 0)
                {
                    return achieved switch
                    {
                        D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_12_2 => "DirectX 12 Ultimate (Feature Level 12_2)",
                        D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_12_1 => "DirectX 12 (Feature Level 12_1)",
                        D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_12_0 => "DirectX 12 (Feature Level 12_0)",
                        D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_1 => "DirectX 11.1 (Feature Level 11_1)",
                        D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_0 => "DirectX 11 (Feature Level 11_0)",
                        _ => "DirectX 12"
                    };
                }
            }
            catch
            {
                // Fällt unten auf den sicheren Standardwert zurück
            }
            finally
            {
                if (context != IntPtr.Zero) Marshal.Release(context);
                if (device != IntPtr.Zero) Marshal.Release(device);
            }

            // Ab Windows 10 (unsere TargetPlatformMinVersion) ist DirectX 12
            // immer Bestandteil des Betriebssystems - sicherer Fallback,
            // falls die Geräteerstellung aus irgendeinem Grund fehlschlägt.
            return "DirectX 12";
        }

        private const uint D3D11_SDK_VERSION = 7;

        private enum D3D_DRIVER_TYPE
        {
            D3D_DRIVER_TYPE_UNKNOWN = 0,
            D3D_DRIVER_TYPE_HARDWARE = 1,
            D3D_DRIVER_TYPE_REFERENCE = 2,
            D3D_DRIVER_TYPE_NULL = 3,
            D3D_DRIVER_TYPE_SOFTWARE = 4,
            D3D_DRIVER_TYPE_WARP = 5
        }

        private enum D3D_FEATURE_LEVEL : uint
        {
            D3D_FEATURE_LEVEL_11_0 = 0xb000,
            D3D_FEATURE_LEVEL_11_1 = 0xb100,
            D3D_FEATURE_LEVEL_12_0 = 0xc000,
            D3D_FEATURE_LEVEL_12_1 = 0xc100,
            D3D_FEATURE_LEVEL_12_2 = 0xc200
        }

        [DllImport("d3d11.dll")]
        private static extern int D3D11CreateDevice(
            IntPtr pAdapter,
            D3D_DRIVER_TYPE driverType,
            IntPtr software,
            uint flags,
            [MarshalAs(UnmanagedType.LPArray)] D3D_FEATURE_LEVEL[] featureLevels,
            uint featureLevelsCount,
            uint sdkVersion,
            out IntPtr device,
            out D3D_FEATURE_LEVEL featureLevel,
            out IntPtr immediateContext);

        // ================= HELPERS =================
        private static bool IsActivated()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT LicenseStatus FROM SoftwareLicensingProduct WHERE PartialProductKey IS NOT NULL");

                foreach (ManagementObject mo in searcher.Get())
                    if (mo["LicenseStatus"]?.ToString() == "1")
                        return true;
            }
            catch { }

            return false;
        }

        private static void Safe(Action a)
        {
            try { a(); } catch { }
        }

        // ================= MEMORY =================
        // WICHTIG: Diese Struct muss 1:1 dem Windows-API-Layout entsprechen.
        // Vorher fehlten "ullAvailPageFile" und "ullTotalVirtual" -> alle
        // nachfolgenden Felder wurden dadurch falsch aus dem Speicher gelesen.
        [StructLayout(LayoutKind.Sequential)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;

            public MEMORYSTATUSEX()
            {
                dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);
    }
}