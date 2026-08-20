using System.Collections.Generic;

namespace WinVora
{
    public static partial class Localization
    {
        private static IReadOnlyDictionary<string, (string De, string En)> GetAutostartStrings() =>
            new Dictionary<string, (string De, string En)>
            {
                ["Autostart.Intro"] = ("Programme, die automatisch mit Windows starten", "Programs that start automatically with Windows"),
                ["Autostart.Loading"] = ("Autostart-Programme werden geladen...", "Loading startup programs..."),
                ["Autostart.Count"] = ("{0} Autostart-Programme", "{0} startup programs"),
                ["Autostart.Active"] = ("Aktiv", "Active"),
                ["Autostart.Disabled"] = ("Deaktiviert", "Disabled"),
                ["Autostart.ChangeFailed"] = ("{0} konnte nicht geändert werden.", "{0} could not be changed."),
                ["Autostart.EnabledMessage"] = ("{0} wurde aktiviert.", "{0} was enabled."),
                ["Autostart.DisabledMessage"] = ("{0} wurde deaktiviert.", "{0} was disabled."),
                ["Autostart.FileMissing"] = ("Datei fehlt", "File missing"),
                ["Autostart.RemoveMissing"] = ("Eintrag entfernen", "Remove entry"),
                ["Autostart.RemoveTitle"] = ("Verwaisten Autostart-Eintrag entfernen?", "Remove orphaned startup entry?"),
                ["Autostart.RemoveMessage"] = ("Der Autostart-Eintrag „{0}“ verweist auf keine vorhandene Datei. Es wird nur der Eintrag entfernt – keine Datei gelöscht.", "The startup entry “{0}” does not point to an existing file. Only the entry is removed; no file is deleted."),
                ["Autostart.RemoveFailed"] = ("Der Autostart-Eintrag konnte nicht entfernt werden.", "The startup entry could not be removed."),
                ["Autostart.Removed"] = ("Der verwaiste Autostart-Eintrag wurde entfernt.", "The orphaned startup entry was removed."),
                ["Autostart.OpenLocation"] = ("Speicherort öffnen", "Open location"),
                ["Autostart.FileGone"] = ("Die Datei ist nicht mehr vorhanden.", "The file no longer exists."),
                ["Autostart.Path"] = ("Pfad: ", "Path: "),
                ["Autostart.SignatureChecking"] = ("Signatur wird geprüft...", "Signature is being checked..."),
                ["Autostart.EmptyTitle"] = ("Keine Autostart-Programme", "No startup programs"),
                ["Autostart.EmptyDescription"] = ("Für diesen Benutzer starten keine Programme automatisch.", "No programs start automatically for this user."),
                ["Autostart.Publisher"] = ("Herausgeber: ", "Publisher: "),
                ["Autostart.Signed"] = ("Signiert", "Signed"),
                ["Autostart.SignatureUnknown"] = ("Signatur unbekannt", "Signature unknown")
            };
    }
}
