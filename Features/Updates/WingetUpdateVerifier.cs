using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

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

        private static string NormalizeVersion(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string normalized = value.Trim().TrimStart('v', 'V').Replace(',', '.');
            if (!Regex.IsMatch(normalized, @"^\d+(?:\.\d+)*$")) return normalized;

            var parts = normalized.Split('.').ToList();
            while (parts.Count > 2 && parts[^1] == "0")
                parts.RemoveAt(parts.Count - 1);
            return string.Join('.', parts.Select(part =>
                long.TryParse(part, out long number) ? number.ToString() : part));
        }

        public static bool NeedsExtendedVerification(WingetPackage package) =>
            package.Id.Contains("GooglePlayGames", StringComparison.OrdinalIgnoreCase) ||
            package.Name.Contains("Google Play Games", StringComparison.OrdinalIgnoreCase) ||
            package.Id.Contains("Claude", StringComparison.OrdinalIgnoreCase) ||
            package.Name.Contains("Claude", StringComparison.OrdinalIgnoreCase);
    }
}
