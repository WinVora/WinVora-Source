using System.Collections.Generic;

namespace WinVora
{
    public static partial class Localization
    {
        private static IReadOnlyDictionary<string, (string De, string En)> GetUpdateStrings() =>
            new Dictionary<string, (string De, string En)>
            {
                ["Update.ForceCloseTitle"] = ("Programm reagiert nicht", "App is not responding"),
                ["Update.ForceCloseMessage"] = (
                    "{0} konnte nicht normal geschlossen werden. Möchtest du das Programm jetzt zwangsweise beenden? Nicht gespeicherte Eingaben können verloren gehen.",
                    "{0} could not be closed normally. Do you want to force close it now? Unsaved work may be lost."),
                ["Update.ForceCloseAction"] = ("Zwangsweise beenden", "Force close"),
                ["Update.CloseCancelled"] = ("Das Programm wurde nicht beendet. Das Update wurde abgebrochen.", "The app was not closed. The update was cancelled."),
                ["Update.CloseFailed"] = ("{0} konnte nicht geschlossen werden. Schließe das Programm manuell und versuche es erneut.", "{0} could not be closed. Close the app manually and try again."),
                ["Settings.IntervalSecond"] = ("{0} Sekunde", "{0} second"),
                ["Settings.IntervalSeconds"] = ("{0} Sekunden", "{0} seconds"),
                ["Storage.AnalysisResults"] = ("{0} Ergebnisse · {1:0.0} Sekunden", "{0} results · {1:0.0} seconds"),
                ["Storage.FoldersChecked"] = ("{0} Ordner geprüft", "{0} folders checked"),
                ["Storage.AnalysisCancelled"] = ("Analyse abgebrochen.", "Analysis cancelled."),
                ["Storage.AnalysisFailed"] = ("Die Analyse konnte nicht abgeschlossen werden.", "The analysis could not be completed."),
                ["Storage.AnalyzeAgain"] = ("Erneut analysieren", "Analyze again"),
                ["Storage.AnalysisIntro"] = ("Durchsucht persönliche Ordner nur auf Knopfdruck. Es wird nichts gelöscht.", "Scans your personal folders on demand. Nothing is deleted."),
                ["Storage.RiskLegend"] = ("Risiko: Normal = persönlicher Ordner · Prüfen = Anwendungsdaten · System = Windows- oder Programmdateien", "Risk: Normal = personal folder · Caution = application data · System = Windows or program files"),
                ["Storage.RiskTooltip"] = ("Die Risikoeinstufung ist nur ein Hinweis. WinVora löscht diese Ordner niemals automatisch.", "Risk is informational only. WinVora does not delete these folders automatically."),
                ["Storage.CachedResult"] = ("Zwischengespeichert vom {0}", "Cached result from {0}"),
                ["Storage.AnalysisHeader"] = ("Speicheranalyse", "Storage analysis")
            };
    }
}
