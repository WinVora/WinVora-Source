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
                using var defender = CreateWmiSearcher(@"root\Microsoft\Windows\Defender",
                    "SELECT AntivirusEnabled, RealTimeProtectionEnabled FROM MSFT_MpComputerStatus");
                s.DefenderStatus = en ? "Unknown" : "Unbekannt";

                foreach (ManagementObject mo in defender.Get())
                {
                    var av = Convert.ToBoolean(mo["AntivirusEnabled"]);
                    var rt = Convert.ToBoolean(mo["RealTimeProtectionEnabled"]);
                    s.DefenderStatus = av && rt ? (en ? "Active" : "Aktiv") : (en ? "Partial/Inactive" : "Teilweise/Inaktiv");
                    break;
                }
                if (s.DefenderStatus.Contains("Inactive", StringComparison.OrdinalIgnoreCase) ||
                    s.DefenderStatus.Contains("Inaktiv", StringComparison.OrdinalIgnoreCase))
                    ApplyRegisteredAntivirusFallback(s, en);
            }
            catch
            {
                s.DefenderStatus = en ? "Unknown" : "Unbekannt";
                ApplyRegisteredAntivirusFallback(s, en);
            }
        }

        private static void FillFirewall(SystemInfoSnapshot s, bool en)
        {
            try
            {
                using var fw = CreateWmiSearcher(@"root\StandardCimv2", "SELECT Enabled FROM MSFT_NetFirewallProfile");
                bool anyEnabled = false;

                foreach (ManagementObject mo in fw.Get())
                {
                    if (Convert.ToBoolean(mo["Enabled"])) anyEnabled = true;
                }

                s.FirewallStatus = anyEnabled ? (en ? "Active" : "Aktiv") : (en ? "Disabled" : "Deaktiviert");
            }
            catch
            {
                // Die WMI-Abfrage benötigt auf einigen Systemen erhöhte
                // Rechte. Die Profilwerte in der Registry sind lesbar und ein
                // zuverlässiger Fallback für den Dashboardstatus.
                try
                {
                    string[] profiles = { "DomainProfile", "PublicProfile", "StandardProfile" };
                    bool enabled = profiles.Any(profile =>
                    {
                        using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\{profile}");
                        return Convert.ToInt32(key?.GetValue("EnableFirewall", 0)) == 1;
                    });
                    s.FirewallStatus = enabled ? (en ? "Active" : "Aktiv") : (en ? "Unknown" : "Unbekannt");
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

        private static void ApplyRegisteredAntivirusFallback(SystemInfoSnapshot s, bool en)
        {
            try
            {
                using var products = CreateWmiSearcher(@"root\SecurityCenter2", "SELECT displayName FROM AntiVirusProduct");
                var names = products.Get().Cast<ManagementObject>()
                    .Select(item => item["displayName"]?.ToString())
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .ToList();
                if (names.Count > 0)
                    s.DefenderStatus = (en ? "Protection registered: " : "Schutz registriert: ") + string.Join(", ", names);
            }
            catch (Exception ex) { Logger.LogErrorOnce("Registrierten Virenschutz lesen", ex); }
        }

        // ================= BOARD + BIOS + SERIAL =================
    }
}
