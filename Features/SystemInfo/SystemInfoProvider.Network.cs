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
    }
}

