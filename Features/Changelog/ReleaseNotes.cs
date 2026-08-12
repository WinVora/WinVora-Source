namespace WinVora
{
    internal static class ReleaseNotes
    {
        public const string CurrentGerman =
            "VERBESSERUNGEN\n" +
            "• Neue Seite Veränderungen zeigt installierte, entfernte und aktualisierte Programme\n" +
            "• Neue und entfernte Autostart-Einträge werden seit der letzten Prüfung erkannt\n" +
            "• Speicherfresser-Alarm warnt, wenn Downloads, Desktop oder Dokumente stark wachsen\n" +
            "• Das Dashboard fasst wichtige Veränderungen direkt zusammen\n" +
            "• Systeminformationen werden bereichsweise gespeichert und gezielt aktualisiert\n" +
            "• Programmlisten können als TXT oder CSV gesichert werden\n" +
            "• Ausführlichere Fehlerberichte erleichtern die Diagnose bei Problemen\n\n" +
            "UI\n" +
            "• Buttons, Suchfelder und Fortschrittsanzeigen verwenden ein einheitliches WinVora-Lila\n" +
            "• Farben für Erfolg, Warnungen und unbekannte Zustände sind im Hell- und Dunkelmodus besser lesbar\n" +
            "• Systeminformationen zeigen den Zeitpunkt der letzten Prüfung\n" +
            "• Lade-, Leer-, Fehler- und Sicherheitszustände sind verständlicher beschriftet\n\n" +
            "SICHERHEIT UND DIAGNOSE\n" +
            "• TPM, Defender, Firewall, Secure Boot und BitLocker werden unabhängig voneinander geprüft\n" +
            "• Langsame Systemabfragen blockieren nicht mehr die gesamten Systeminformationen\n" +
            "• Supportberichte werden vor dem Speichern angezeigt und anonymisiert\n" +
            "• Einstellungen erhalten vor einem Import automatisch eine Sicherung\n" +
            "• Protokolldateien werden begrenzt und ältere Dateien automatisch rotiert\n\n" +
            "BUGFIXES\n" +
            "• WinGet-Prüfungen lassen sich sauber abbrechen und melden technische Fehler zuverlässiger\n" +
            "• Das Schließen während des Starts beendet laufende Hintergrundabfragen sauber\n" +
            "• Ein Timeout bei einer Sicherheitsprüfung verwirft nicht mehr alle Systeminformationen\n" +
            "• Verlauf, TPM-Erkennung, Deinstallation und parallele Ladevorgänge wurden stabilisiert";

        public const string CurrentEnglish =
            "IMPROVEMENTS\n" +
            "• New Changes page shows installed, removed and updated programs\n" +
            "• New and removed startup entries are detected between checks\n" +
            "• Storage hog alerts flag strong growth in Downloads, Desktop or Documents\n" +
            "• The dashboard summarizes important changes directly\n" +
            "• System information is cached by category and refreshed selectively\n" +
            "• Program lists can be saved as TXT or CSV\n" +
            "• More detailed error reports make troubleshooting easier\n\n" +
            "INTERFACE\n" +
            "• Buttons, search fields and progress indicators now use consistent WinVora purple\n" +
            "• Success, warning and unknown states are easier to read in light and dark mode\n" +
            "• System information displays the time of the last check\n" +
            "• Loading, empty, error and security states use clearer wording\n\n" +
            "SECURITY AND DIAGNOSTICS\n" +
            "• TPM, Defender, Firewall, Secure Boot and BitLocker are checked independently\n" +
            "• Slow system checks no longer block all system information\n" +
            "• Support reports are previewed and anonymized before saving\n" +
            "• Existing settings are backed up before an import\n" +
            "• Log files are limited and older files rotate automatically\n\n" +
            "BUG FIXES\n" +
            "• WinGet checks can be cancelled cleanly and report technical failures more reliably\n" +
            "• Closing during startup now stops background checks cleanly\n" +
            "• A security timeout no longer discards all system information\n" +
            "• History, TPM detection, uninstall and parallel loading were stabilized";
    }
}
