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
            "• WinVora zeigt beim Start vier nachvollziehbare Ladephasen an\n" +
            "• Letzte Systemwerte erscheinen sofort und werden anschließend aktualisiert\n" +
            "• Langsame Updateprüfungen laufen nach kurzer Wartezeit im Hintergrund weiter\n" +
            "• Programmlisten können als TXT oder CSV gesichert werden\n\n" +
            "UI\n" +
            "• Suchfelder, Statusanzeigen und kleine Fenster reagieren übersichtlicher\n" +
            "• Systeminformationen zeigen den Zeitpunkt der letzten Prüfung\n" +
            "• Lade-, Leer-, Fehler- und Sicherheitszustände sind verständlicher beschriftet\n\n" +
            "SICHERHEIT UND DIAGNOSE\n" +
            "• Supportberichte werden vor dem Speichern angezeigt und anonymisiert\n" +
            "• Einstellungen erhalten vor einem Import automatisch eine Sicherung\n" +
            "• Protokolldateien werden begrenzt und ältere Dateien automatisch rotiert\n\n" +
            "BUGFIXES\n" +
            "• Der Start wird nicht mehr dauerhaft von WinGet blockiert\n" +
            "• Sicherheitsstatus unterscheidet Probleme von nicht prüfbaren Werten\n" +
            "• Verlauf, TPM-Erkennung, Deinstallation und parallele Ladevorgänge wurden stabilisiert";

        public const string CurrentEnglish =
            "IMPROVEMENTS\n" +
            "• New Changes page shows installed, removed and updated programs\n" +
            "• New and removed startup entries are detected between checks\n" +
            "• Storage hog alerts flag strong growth in Downloads, Desktop or Documents\n" +
            "• The dashboard summarizes important changes directly\n" +
            "• WinVora displays four clear startup phases\n" +
            "• Previous system values appear immediately and refresh afterwards\n" +
            "• Slow update checks continue in the background after a short wait\n" +
            "• Program lists can be saved as TXT or CSV\n\n" +
            "INTERFACE\n" +
            "• Search fields, status displays and compact windows are easier to use\n" +
            "• System information displays the time of the last check\n" +
            "• Loading, empty, error and security states use clearer wording\n\n" +
            "SECURITY AND DIAGNOSTICS\n" +
            "• Support reports are previewed and anonymized before saving\n" +
            "• Existing settings are backed up before an import\n" +
            "• Log files are limited and older files rotate automatically\n\n" +
            "BUG FIXES\n" +
            "• WinGet no longer blocks startup indefinitely\n" +
            "• Security status distinguishes problems from unavailable checks\n" +
            "• History, TPM detection, uninstall and parallel loading were stabilized";
    }
}
