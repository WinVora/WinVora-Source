using System;

namespace WinVora
{
    internal static class WingetErrorTranslator
    {
        public static bool ContainsRestartRequired(string output) =>
            output.Contains("restart required", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("reboot required", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("neustart erforderlich", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("restart is needed", StringComparison.OrdinalIgnoreCase);

        public static string GetFriendlyMessage(int exitCode, string output, WingetUpdateStatus status)
        {
            bool en = Localization.CurrentLanguage == "en";
            string normalized = output.ToLowerInvariant();

            if (status == WingetUpdateStatus.Cancelled || normalized.Contains("cancelled") ||
                normalized.Contains("canceled") || normalized.Contains("abgebrochen") || exitCode == 1602)
                return en ? "The installer was cancelled." : "Der Installer wurde abgebrochen.";

            if (status == WingetUpdateStatus.RestartRequired)
                return en ? "Installed successfully. A Windows restart is required." : "Erfolgreich installiert. Ein Windows-Neustart ist erforderlich.";

            if (exitCode == 0)
                return en ? "Installed successfully." : "Erfolgreich installiert.";

            if (normalized.Contains("0x80072") || normalized.Contains("internet") ||
                normalized.Contains("network") || normalized.Contains("netzwerk") ||
                normalized.Contains("name resolution"))
                return en ? "No connection to the download server. Check your internet connection." : "Keine Verbindung zum Downloadserver. Prüfe deine Internetverbindung.";

            if (normalized.Contains("0x80070005") || normalized.Contains("access denied") ||
                normalized.Contains("zugriff verweigert") || normalized.Contains("administrator"))
                return en ? "Administrator permission is required." : "Für dieses Update werden Administratorrechte benötigt.";

            if (normalized.Contains("hash") && (normalized.Contains("match") || normalized.Contains("stimm")))
                return en ? "The downloaded installer failed its security check." : "Der heruntergeladene Installer hat die Sicherheitsprüfung nicht bestanden.";

            if (normalized.Contains("no applicable installer") || normalized.Contains("kein zutreffendes installationsprogramm"))
                return en ? "No compatible installer is available for this PC." : "Für diesen PC ist kein passender Installer verfügbar.";

            // APPINSTALLER_CLI_ERROR_SHELLEXEC_INSTALL_FAILED: Der Download
            // kann bereits erfolgreich gewesen sein, Windows konnte den
            // Hersteller-Installer anschließend aber nicht starten.
            if (unchecked((uint)exitCode) == 0x8A150006 || normalized.Contains("0x8a150006") ||
                normalized.Contains("shellexecute failed") || normalized.Contains("running shellexecute failed"))
                return en
                    ? "Windows could not start the installer. Close the affected app, try again and approve an administrator prompt if one appears."
                    : "Windows konnte den Installer nicht starten. Schließe das betroffene Programm, versuche es erneut und bestätige eine mögliche Administratorabfrage.";

            return en
                ? $"The update failed (code 0x{unchecked((uint)exitCode):X8})."
                : $"Das Update ist fehlgeschlagen (Code 0x{unchecked((uint)exitCode):X8}).";
        }
    }
}
