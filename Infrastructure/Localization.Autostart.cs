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
