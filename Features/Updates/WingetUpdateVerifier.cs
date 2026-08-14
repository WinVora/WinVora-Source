using System;
using System.Collections.Generic;
using System.Linq;

namespace WinVora
{
    internal static class WingetUpdateVerifier
    {
        public static bool IsStillUnchanged(WingetPackage installedAttempt, IEnumerable<WingetPackage> available)
        {
            var remaining = available.FirstOrDefault(package =>
                package.Id.Equals(installedAttempt.Id, StringComparison.OrdinalIgnoreCase));

            if (remaining == null)
                return false;

            // Ein Paket kann direkt nach der Installation bereits ein weiteres
            // Update anbieten. Nur dieselbe installierte Ausgangsversion zeigt,
            // dass der gerade ausgeführte Installer nichts geändert hat.
            return VersionsEqual(remaining.Version, installedAttempt.Version);
        }

        public static bool VersionsEqual(string? left, string? right) =>
            string.Equals(NormalizeVersion(left), NormalizeVersion(right), StringComparison.OrdinalIgnoreCase);

        private static string NormalizeVersion(string? value) =>
            string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().TrimStart('v', 'V');
    }
}
