using System;
using System.Collections.Generic;
using System.Linq;

namespace WinVora
{
    internal static class SystemInfoFormatter
    {
        public static string Device(SystemInfoSnapshot s) => Join(
            ("Computername", s.ComputerName),
            ("Benutzername", s.UserName),
            ("Hersteller / Modell", $"{s.Manufacturer} {s.Model}".Trim()),
            ("Seriennummer", s.SerialNumber),
            ("Architektur", s.Architecture));

        public static string OperatingSystem(SystemInfoSnapshot s) => Join(
            ("Windows", s.WindowsEdition),
            ("Version / Build", $"{s.WindowsVersion} (Build {s.BuildNumber})"),
            ("Installationsdatum", s.InstallDate),
            ("Letztes Update", s.LastUpdate),
            ("Aktivierung", s.ActivationStatus),
            ("Uptime", s.Uptime),
            (".NET", s.DotNetVersion),
            ("DirectX", s.DirectXVersion));

        public static string Cpu(SystemInfoSnapshot s, bool en) => Join(
            ("CPU", s.CpuName),
            (en ? "Cores / Threads / Clock" : "Kerne / Threads / Takt",
                $"{s.CpuCores} / {s.CpuThreads} / {s.CpuClock}"));

        public static string Ram(SystemInfoSnapshot s, bool en) => Join(
            (en ? "Installed" : "Installiert", s.RamTotal),
            (en ? "Used" : "Belegt", s.RamUsed),
            (en ? "Free" : "Frei", s.RamFree));

        public static string Board(SystemInfoSnapshot s) => Join(
            ("Mainboard", s.Mainboard),
            ("BIOS", s.BiosVersion));

        public static string Security(SystemInfoSnapshot s) => Join(
            ("Secure Boot", s.SecureBoot),
            ("TPM", s.TpmVersion),
            ("Virtualisierung", s.Virtualization),
            ("Windows Defender", s.DefenderStatus),
            ("Firewall", s.FirewallStatus),
            ("BitLocker", s.BitLockerStatus));

        public static string Gpus(SystemInfoSnapshot s) =>
            s.Gpus.Length == 0 ? "N/A" : string.Join(Environment.NewLine, s.Gpus);

        public static string Drives(SystemInfoSnapshot s) => string.Join(
            Environment.NewLine + Environment.NewLine,
            s.Drives.Select(drive => Join(
                ("Laufwerk", drive.Name),
                ("Gesamt", drive.TotalSize),
                ("Frei", drive.FreeSpace))));

        public static string Network(SystemInfoSnapshot s) => string.Join(
            Environment.NewLine + Environment.NewLine,
            s.NetworkAdapters.Select(adapter => Join(
                ("Adapter", adapter.Name),
                ("IPv4", adapter.IPv4),
                ("MAC", adapter.MacAddress),
                ("Gateway", adapter.Gateway),
                ("DNS", adapter.Dns))));

        public static string Battery(SystemInfoSnapshot s) => s.BatteryStatus;

        public static string Card(string header, string description, string? content) =>
            string.Join(Environment.NewLine,
                new[] { header, description, content }.Where(value => !string.IsNullOrWhiteSpace(value)));

        private static string Join(params (string Label, string Value)[] values) =>
            string.Join(Environment.NewLine, values.Select(value => $"{value.Label}: {value.Value}"));
    }
}
