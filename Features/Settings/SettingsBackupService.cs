using System;
using System.IO;

namespace WinVora
{
    internal static class SettingsBackupService
    {
        public static string CreateAutomatic(AppSettings settings, string reason)
        {
            string settingsPath = AppSettings.GetSettingsFilePath();
            string backupDirectory = Path.Combine(Path.GetDirectoryName(settingsPath)!, "Backups");
            Directory.CreateDirectory(backupDirectory);

            string safeReason = string.Concat(reason.Select(character =>
                char.IsLetterOrDigit(character) || character == '-' ? character : '-'));
            string backupPath = Path.Combine(
                backupDirectory,
                $"settings-{safeReason}-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");

            if (File.Exists(settingsPath))
                File.Copy(settingsPath, backupPath, overwrite: false);
            else
                settings.SaveCopy(backupPath);

            Logger.Log($"Automatische Einstellungssicherung erstellt: {backupPath}");
            return backupPath;
        }
    }
}
