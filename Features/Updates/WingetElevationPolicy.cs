using System;
using System.Collections.Generic;

namespace WinVora
{
    internal static class WingetElevationPolicy
    {
        private static readonly HashSet<string> PackagesRequiringElevation = new(
            StringComparer.OrdinalIgnoreCase)
        {
            // Die MSIX-Aktualisierung von Claude liefert ohne erhöhten
            // WinGet-Prozess APPX_E_PACKAGE_NOT_FOUND_FOR_USER (0x80073D28).
            "Anthropic.Claude",
            // Das Microsoft-MSI schreibt geschützte Updatekomponenten und
            // endet ohne erhöhte Rechte intern mit 0x80070005 / MSI 1603.
            "Microsoft.UpdateHealthTools"
        };

        public static bool RequiresElevationBeforeInstall(string packageId) =>
            PackagesRequiringElevation.Contains(packageId);

        public static bool RequiresApplicationShutdown(string packageId) =>
            packageId.Equals("Anthropic.Claude", StringComparison.OrdinalIgnoreCase);
    }
}
