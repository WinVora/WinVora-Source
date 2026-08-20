using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace WinVora
{
    public static partial class SystemInfoProvider
    {
        private static void FillSecurity(SystemInfoSnapshot s)
        {
            bool en = Localization.CurrentLanguage == "en";

            // Diese Quellen sind voneinander unabhängig. Besonders TPM oder
            // BitLocker können ohne passende Rechte bis zum WMI-Timeout warten;
            // Defender und Firewall sollen dadurch nicht verzögert werden.
            Parallel.Invoke(
                () => FillSecureBoot(s, en),
                () => FillTpm(s, en),
                () => FillDefender(s, en),
                () => FillFirewall(s, en),
                () => FillBitLocker(s, en));
        }

        private static void FillSecureBoot(SystemInfoSnapshot s, bool en)
        {
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
        }

        private static void FillTpm(SystemInfoSnapshot s, bool en)
        {
            try
            {
                using var tpm = CreateWmiSearcher(@"root\CIMV2\Security\MicrosoftTpm", "SELECT SpecVersion FROM Win32_Tpm");
                s.TpmVersion = en ? "Not available" : "Nicht verfügbar";

                foreach (ManagementObject mo in tpm.Get())
                {
                    s.TpmVersion = mo["SpecVersion"]?.ToString() ?? (en ? "TPM present" : "TPM vorhanden");
                    break;
                }
                if (s.TpmVersion is "Not available" or "Nicht verfügbar")
                    s.TpmVersion = DetectTpmFromAcpi(en);
            }
            catch
            {
                s.TpmVersion = DetectTpmFromAcpi(en);
            }
        }

        private static void FillDefender(SystemInfoSnapshot s, bool en)
        {
            try
            {
                s.DefenderStatus = SecurityStatusEvaluator.Format(ReadAntivirusState(), en);
            }
            catch (Exception ex)
            {
                Logger.LogErrorOnce("Defenderstatus lesen", ex);
                s.DefenderStatus = en ? "Unknown" : "Unbekannt";
            }
        }

        private static SecurityComponentState ReadAntivirusState()
        {
            // Defender selbst ist die verlässlichste Quelle. SecurityCenter2
            // listet auf manchen Windows-Installationen zeitweise gar kein
            // Produkt auf und führte dadurch zu einem falschen „Nicht prüfbar“.
            try
            {
                using var defender = CreateWmiSearcher(@"root\Microsoft\Windows\Defender",
                    "SELECT AntivirusEnabled, RealTimeProtectionEnabled FROM MSFT_MpComputerStatus");
                foreach (ManagementObject item in defender.Get())
                {
                    bool antivirusEnabled = Convert.ToBoolean(item["AntivirusEnabled"]);
                    bool realtimeEnabled = Convert.ToBoolean(item["RealTimeProtectionEnabled"]);
                    return antivirusEnabled && realtimeEnabled
                        ? SecurityComponentState.Active
                        : SecurityComponentState.Partial;
                }
            }
            catch (Exception ex)
            {
                Logger.LogErrorOnce("Direkten Defenderstatus lesen", ex);
            }

            try
            {
                using var products = CreateWmiSearcher(@"root\SecurityCenter2",
                    "SELECT displayName, productState FROM AntiVirusProduct");
                bool foundProduct = false;
                bool foundActiveProduct = false;
                foreach (ManagementObject item in products.Get())
                {
                    foundProduct = true;
                    int productState = Convert.ToInt32(item["productState"]);
                    int realtimeState = (productState >> 8) & 0xFF;
                    if (realtimeState is 0x10 or 0x11)
                        foundActiveProduct = true;
                }

                if (foundActiveProduct)
                    return SecurityComponentState.Active;
                if (foundProduct)
                    return SecurityComponentState.Partial;
            }
            catch (Exception ex)
            {
                Logger.LogErrorOnce("Registrierten Virenschutzstatus lesen", ex);
            }

            // Manche Windows-Systeme liefern weder über den Defender-Namespace
            // noch über SecurityCenter2 einen Datensatz. Der Dienststatus ist
            // dann noch immer aussagekräftiger als ein falsches „Nicht prüfbar“.
            if (TryGetServiceRunning("WinDefend", out bool defenderRunning))
                return defenderRunning
                    ? SecurityComponentState.Active
                    : SecurityComponentState.Partial;

            return SecurityComponentState.Unknown;
        }

        private static SecurityComponentState ReadFastAntivirusState()
        {
            // Für den schnellen Dashboard-Status wird absichtlich nur der
            // native Dienststatus gelesen. Ist Defender aktiv, ist das Ergebnis
            // eindeutig. Ist er nicht aktiv, kann ein Drittanbieter-Virenschutz
            // übernommen haben; deshalb hier konservativ "nicht prüfbar" statt
            // fälschlich ein Sicherheitsproblem zu melden.
            if (!TryGetServiceRunning("WinDefend", out bool defenderRunning))
                return SecurityComponentState.Unknown;
            return defenderRunning ? SecurityComponentState.Active : SecurityComponentState.Unknown;
        }

        private static bool TryGetServiceRunning(string serviceName, out bool running)
        {
            running = false;
            nint manager = OpenSCManager(null, null, 0x0001); // SC_MANAGER_CONNECT
            if (manager == 0)
                return false;

            try
            {
                nint service = OpenService(manager, serviceName, 0x0004); // SERVICE_QUERY_STATUS
                if (service == 0)
                    return false;

                try
                {
                    if (!QueryServiceStatus(service, out ServiceStatus status))
                        return false;
                    running = status.CurrentState == 0x00000004; // SERVICE_RUNNING
                    return true;
                }
                finally
                {
                    CloseServiceHandle(service);
                }
            }
            finally
            {
                CloseServiceHandle(manager);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ServiceStatus
        {
            public uint ServiceType;
            public uint CurrentState;
            public uint ControlsAccepted;
            public uint Win32ExitCode;
            public uint ServiceSpecificExitCode;
            public uint CheckPoint;
            public uint WaitHint;
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern nint OpenSCManager(string? machineName, string? databaseName, uint desiredAccess);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern nint OpenService(nint serviceManager, string serviceName, uint desiredAccess);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryServiceStatus(nint service, out ServiceStatus serviceStatus);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseServiceHandle(nint handle);

        private static void FillFirewall(SystemInfoSnapshot s, bool en)
        {
            try
            {
                using var fw = CreateWmiSearcher(@"root\StandardCimv2", "SELECT Enabled FROM MSFT_NetFirewallProfile");
                var states = new System.Collections.Generic.List<bool?>();

                foreach (ManagementObject mo in fw.Get())
                    states.Add(mo["Enabled"] == null ? null : Convert.ToBoolean(mo["Enabled"]));

                s.FirewallStatus = SecurityStatusEvaluator.Format(EvaluateFirewallProfileState(states), en);
            }
            catch
            {
                // Die WMI-Abfrage benötigt auf einigen Systemen erhöhte
                // Rechte. Die Profilwerte in der Registry sind lesbar und ein
                // zuverlässiger Fallback für den Dashboardstatus.
                try
                {
                    string[] profiles = { "DomainProfile", "PublicProfile", "StandardProfile" };
                    var states = profiles.Select(profile =>
                    {
                        using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\{profile}");
                        object? value = key?.GetValue("EnableFirewall");
                        return value == null ? (bool?)null : Convert.ToInt32(value) == 1;
                    }).ToArray();
                    s.FirewallStatus = SecurityStatusEvaluator.Format(EvaluateFirewallProfileState(states), en);
                }
                catch (Exception ex)
                {
                    Logger.LogErrorOnce("Firewallstatus aus Registry lesen", ex);
                    s.FirewallStatus = en ? "Unknown" : "Unbekannt";
                }
            }
        }

        private static void FillBitLocker(SystemInfoSnapshot s, bool en)
        {
            try
            {
                using var bl = CreateWmiSearcher(@"root\CIMV2\Security\MicrosoftVolumeEncryption",
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

        private static string DetectTpmFromAcpi(bool en)
        {
            try
            {
                using var acpi = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\ACPI");
                var hardwareIds = acpi?.GetSubKeyNames() ?? Array.Empty<string>();
                if (hardwareIds.Any(id => id.Equals("MSFT0101", StringComparison.OrdinalIgnoreCase)))
                    return en ? "TPM 2.0 present" : "TPM 2.0 vorhanden";
                if (hardwareIds.Any(id => id.Equals("IFX0102", StringComparison.OrdinalIgnoreCase) ||
                                          id.Contains("TPM", StringComparison.OrdinalIgnoreCase)))
                    return en ? "TPM present" : "TPM vorhanden";
            }
            catch (Exception ex) { Logger.LogErrorOnce("TPM über ACPI erkennen", ex); }

            return en
                ? "Not available (administrator rights may be required)"
                : "Nicht verfügbar (ggf. Administratorrechte erforderlich)";
        }

        // ================= BOARD + BIOS + SERIAL =================
    }
}
