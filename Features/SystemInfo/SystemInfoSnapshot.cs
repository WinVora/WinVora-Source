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

        public SystemInfoSnapshot Clone()
        {
            var copy = new SystemInfoSnapshot();
            foreach (SystemInfoSection section in Enum.GetValues<SystemInfoSection>())
                copy.CopySectionFrom(this, section);
            return copy;
        }

        public void CopySectionFrom(SystemInfoSnapshot source, SystemInfoSection section)
        {
            switch (section)
            {
                case SystemInfoSection.Device:
                    ComputerName=source.ComputerName; UserName=source.UserName; Manufacturer=source.Manufacturer; Model=source.Model; SerialNumber=source.SerialNumber; Architecture=source.Architecture; Virtualization=source.Virtualization; DotNetVersion=source.DotNetVersion; break;
                case SystemInfoSection.OperatingSystem:
                    WindowsEdition=source.WindowsEdition; WindowsVersion=source.WindowsVersion; BuildNumber=source.BuildNumber; InstallDate=source.InstallDate; LastUpdate=source.LastUpdate; ActivationStatus=source.ActivationStatus; DirectXVersion=source.DirectXVersion; Uptime=source.Uptime; break;
                case SystemInfoSection.Cpu: CpuName=source.CpuName; CpuCores=source.CpuCores; CpuThreads=source.CpuThreads; CpuClock=source.CpuClock; break;
                case SystemInfoSection.Ram: RamTotal=source.RamTotal; RamFree=source.RamFree; RamUsed=source.RamUsed; break;
                case SystemInfoSection.Board: Mainboard=source.Mainboard; BiosVersion=source.BiosVersion; SerialNumber=source.SerialNumber; break;
                case SystemInfoSection.Security: SecureBoot=source.SecureBoot; TpmVersion=source.TpmVersion; DefenderStatus=source.DefenderStatus; FirewallStatus=source.FirewallStatus; BitLockerStatus=source.BitLockerStatus; break;
                case SystemInfoSection.Gpu: Gpus=source.Gpus.ToArray(); break;
                case SystemInfoSection.Drives: Drives=source.Drives.Select(d => new DriveSummary { Name=d.Name, TotalSize=d.TotalSize, FreeSpace=d.FreeSpace }).ToArray(); break;
                case SystemInfoSection.Network: NetworkAdapters=source.NetworkAdapters.Select(n => new NetworkSummary { Name=n.Name, IPv4=n.IPv4, IPv6=n.IPv6, MacAddress=n.MacAddress, Gateway=n.Gateway, Dns=n.Dns }).ToArray(); break;
                case SystemInfoSection.Battery: BatteryStatus=source.BatteryStatus; break;
            }
        }
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
