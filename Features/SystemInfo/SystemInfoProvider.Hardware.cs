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
                using var cs = CreateWmiSearcher("SELECT Manufacturer, Model, HypervisorPresent FROM Win32_ComputerSystem");

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
        private static void FillCpu(SystemInfoSnapshot s)
        {
            Safe(() =>
            {
                using var searcher = CreateWmiSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor");

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
                using var searcher = CreateWmiSearcher("SELECT Name FROM Win32_VideoController");

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
        private static void FillBoardAndBios(SystemInfoSnapshot s)
        {
            Safe(() =>
            {
                using var board = CreateWmiSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard");

                foreach (ManagementObject mo in board.Get())
                {
                    s.Mainboard = $"{mo["Manufacturer"]} {mo["Product"]}";
                    break;
                }

                using var bios = CreateWmiSearcher("SELECT SerialNumber, SMBIOSBIOSVersion FROM Win32_BIOS");

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
                using var searcher = CreateWmiSearcher("SELECT EstimatedChargeRemaining, BatteryStatus FROM Win32_Battery");

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
