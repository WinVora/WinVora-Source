using System;

namespace WinVora
{
    public class SystemInfoSnapshot
    {
        // ================= DEVICE =================
        public string ComputerName { get; set; } = "";
        public string UserName { get; set; } = "";
        public string Manufacturer { get; set; } = "";
        public string Model { get; set; } = "";
        public string SerialNumber { get; set; } = "";
        public string Architecture { get; set; } = "";

        // ================= OS =================
        public string WindowsEdition { get; set; } = "";
        public string WindowsVersion { get; set; } = "";
        public string BuildNumber { get; set; } = "";
        public string InstallDate { get; set; } = "";
        public string LastUpdate { get; set; } = "";
        public string ActivationStatus { get; set; } = "";
        public string DotNetVersion { get; set; } = "";
        public string DirectXVersion { get; set; } = "";
        public string Uptime { get; set; } = "";

        // ================= CPU =================
        public string CpuName { get; set; } = "";
        public string CpuCores { get; set; } = "";
        public string CpuThreads { get; set; } = "";
        public string CpuClock { get; set; } = "";

        // ================= RAM =================
        public string RamTotal { get; set; } = "";
        public string RamFree { get; set; } = "";
        public string RamUsed { get; set; } = "";

        // ================= MAINBOARD =================
        public string Mainboard { get; set; } = "";
        public string BiosVersion { get; set; } = "";

        // ================= SECURITY =================
        public string SecureBoot { get; set; } = "";
        public string TpmVersion { get; set; } = "";
        public string Virtualization { get; set; } = "";
        public string DefenderStatus { get; set; } = "";
        public string FirewallStatus { get; set; } = "";
        public string BitLockerStatus { get; set; } = "";

        // ================= GPU =================
        public string[] Gpus { get; set; } = Array.Empty<string>();

        // ================= STORAGE =================
        public DriveSummary[] Drives { get; set; } = Array.Empty<DriveSummary>();

        // ================= NETWORK =================
        public NetworkSummary[] NetworkAdapters { get; set; } = Array.Empty<NetworkSummary>();

        // ================= POWER =================
        public string BatteryStatus { get; set; } = "";
    }

    // ================= DRIVE =================
    public class DriveSummary
    {
        public string Name { get; set; } = "";
        public string TotalSize { get; set; } = "";
        public string FreeSpace { get; set; } = "";
    }

    // ================= NETWORK =================
    public class NetworkSummary
    {
        public string Name { get; set; } = "";
        public string IPv4 { get; set; } = "";
        public string IPv6 { get; set; } = "";
        public string MacAddress { get; set; } = "";
        public string Gateway { get; set; } = "";
        public string Dns { get; set; } = "";
    }
}