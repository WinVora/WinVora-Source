using System;
using System.IO;
using System.Net.Http;

namespace WinVora
{
    internal static class UpdateErrorMessageService
    {
        public static string ForCheck(Exception error, bool english) => error switch
        {
            HttpRequestException => english
                ? "GitHub could not be reached. Check your internet connection."
                : "GitHub ist nicht erreichbar. Prüfe deine Internetverbindung.",
            TaskCanceledException => english
                ? "The update check timed out. Please try again."
                : "Die Update-Prüfung hat zu lange gedauert. Bitte versuche es erneut.",
            InvalidDataException => english
                ? "The new version has no installer available yet."
                : "Für die neue Version ist noch kein Installer verfügbar.",
            _ => english
                ? "The update check failed. Please try again later."
                : "Die Update-Prüfung ist fehlgeschlagen. Bitte versuche es später erneut."
        };

        public static string ForInstall(Exception error, bool english) => error switch
        {
            InvalidDataException => english
                ? "The download is damaged or incomplete and was removed."
                : "Der Download ist beschädigt oder unvollständig und wurde entfernt.",
            UnauthorizedAccessException => english
                ? "Windows denied access. Close other WinVora installers and try again."
                : "Windows hat den Zugriff verweigert. Schließe andere WinVora-Installer und versuche es erneut.",
            HttpRequestException => english
                ? "The download failed because the server or internet connection is unavailable."
                : "Der Download ist fehlgeschlagen, weil Server oder Internetverbindung nicht erreichbar sind.",
            _ => english
                ? "The update could not be installed. Please try again later."
                : "Das Update konnte nicht installiert werden. Bitte versuche es später erneut."
        };
    }
}
