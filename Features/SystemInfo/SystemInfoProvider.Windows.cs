using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WinVora
{
    public static partial class SystemInfoProvider
    {
        private static void FillOS(SystemInfoSnapshot s)
        {
            bool en = Localization.CurrentLanguage == "en";
            Safe(() =>
            {
                using var searcher = CreateWmiSearcher("SELECT * FROM Win32_OperatingSystem");

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
                using var searcher = CreateWmiSearcher(
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
                using var searcher = CreateWmiSearcher(
                    "SELECT LicenseStatus FROM SoftwareLicensingProduct WHERE PartialProductKey IS NOT NULL");

                foreach (ManagementObject mo in searcher.Get())
                    if (mo["LicenseStatus"]?.ToString() == "1")
                        return true;
            }
            catch (Exception ex) { Logger.LogErrorOnce("Windows-Aktivierung prüfen", ex); }

            return false;
        }

    }
}
