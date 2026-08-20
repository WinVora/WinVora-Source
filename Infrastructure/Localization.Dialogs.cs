using System.Collections.Generic;

namespace WinVora
{
    public static partial class Localization
    {
        private static IReadOnlyDictionary<string, (string De, string En)> GetDialogStrings() =>
            new Dictionary<string, (string De, string En)>
            {
                ["Dialog.Running.Title"] = ("Vorgang läuft noch", "An operation is still running"),
                ["Dialog.Running.Message"] = ("WinVora aktualisiert, analysiert, bereinigt oder wartet auf einen Deinstaller. Beim Schließen werden WinVora-Aufgaben abgebrochen; Hersteller-Deinstaller können geöffnet bleiben.", "WinVora is updating, analyzing, cleaning, or waiting for an uninstaller. Closing cancels WinVora tasks; manufacturer uninstallers may remain open."),
                ["Dialog.Running.Close"] = ("Abbrechen und schließen", "Cancel and close"),
                ["Dialog.Running.Continue"] = ("Weiterlaufen lassen", "Keep running"),
                ["Dialog.Reset.Title"] = ("Einstellungen zurücksetzen?", "Reset settings?"),
                ["Dialog.Reset.Message"] = ("Alle Einstellungen werden auf die Standardwerte zurückgesetzt. Fortfahren?", "All settings will be reset to their defaults. Continue?"),
                ["Dialog.Reset.Action"] = ("Zurücksetzen", "Reset"),
                ["Dialog.KofiMissing.Title"] = ("Ko-fi-Link fehlt noch", "Ko-fi link is missing"),
                ["Dialog.KofiMissing.Message"] = ("Trage den echten Ko-fi-Link in der Anwendungskonfiguration ein.", "Add the correct Ko-fi link to the application configuration."),
                ["Notification.UpdateBlocksNavigation"] = ("Beende oder brich das laufende Update ab, bevor du die Seite wechselst.", "Finish or cancel the running update before changing pages."),
                ["Notification.MissingTextKey"] = ("Text nicht verfügbar", "Text unavailable")
            };
    }
}
