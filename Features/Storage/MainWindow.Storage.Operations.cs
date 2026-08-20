using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace WinVora
{
    public sealed partial class MainWindow
    {

        // Löscht eine oder mehrere Storage-Kategorien. Kategorien, die
        // Admin-Rechte brauchen, laufen über einen kurzen elevierten
        // Hilfsprozess (ein UAC-Prompt für alle zusammen); alles andere läuft
        // direkt im normalen, nicht elevierten Prozess.
        private async Task<(bool success, string message)> DeleteCategoriesAsync(List<StorageCategory> categories)
        {
            var adminCategories = categories.Where(c => c.RequiresAdmin).ToList();
            var normalCategories = categories.Where(c => !c.RequiresAdmin).ToList();

            var messages = new List<string>();
            bool overallSuccess = true;

            foreach (var category in normalCategories)
            {
                var (success, message) = await StorageService.DeleteCategoryAsync(category);
                messages.Add($"{category.Name}: {message}");
                if (!success) overallSuccess = false;
            }

            if (adminCategories.Count > 0)
            {
                var exitCode = await RunElevatedStorageDeleteAsync(adminCategories);

                if (exitCode == 0)
                {
                    messages.Add(adminCategories.Count == 1
                        ? $"{adminCategories[0].Name}: erfolgreich gelöscht (mit Admin-Rechten)."
                        : $"{adminCategories.Count} Admin-Bereiche erfolgreich gelöscht.");
                }
                else if (exitCode == 1223) // ERROR_CANCELLED - Nutzer hat UAC abgelehnt
                {
                    overallSuccess = false;
                    messages.Add("Admin-Rechte wurden nicht erteilt - Admin-pflichtige Bereiche wurden übersprungen.");
                }
                else
                {
                    overallSuccess = false;
                    messages.Add("Einige Admin-pflichtige Bereiche konnten nicht (vollständig) gelöscht werden.");
                }
            }

            return (overallSuccess, string.Join("  •  ", messages));
        }

        // Startet den elevierten Hilfsprozess für Admin-pflichtige Löschungen
        // und liefert dessen Exitcode zurück (0 = alles erfolgreich).
        private async Task<int> RunElevatedStorageDeleteAsync(List<StorageCategory> categories)
        {
            try
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath))
                    throw new InvalidOperationException("Eigener Programmpfad konnte nicht ermittelt werden.");

                var keyList = string.Join(";", categories.Select(c => c.Key));

                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = $"--delete-storage \"{keyList}\"",
                    UseShellExecute = true,
                    Verb = "runas" // löst den UAC-Prompt nur für diesen einen Vorgang aus
                };

                using var proc = Process.Start(psi);
                if (proc != null) await proc.WaitForExitAsync();

                return proc?.ExitCode ?? -1;
            }
            catch (System.ComponentModel.Win32Exception winEx) when (winEx.NativeErrorCode == 1223)
            {
                // Nutzer hat den UAC-Prompt mit "Nein" abgebrochen
                Logger.Log("Elevierte Storage-Löschung vom Nutzer abgebrochen (UAC verweigert).");
                return 1223;
            }
            catch (Exception ex)
            {
                Logger.LogError("RunElevatedStorageDeleteAsync", ex);
                return -1;
            }
        }

        private async void StorageDeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            if (_isDeletingStorage || _isLoadingStorage) return;
            var selected = _storageRows.Where(r => r.Toggle.IsChecked == true).Select(r => r.Category).ToList();

            if (selected.Count == 0)
            {
                StorageProgressPanel.Visibility = Visibility.Visible;
                StorageProgressText.Text = Localization.CurrentLanguage == "en" ? "No categories selected." : "Keine Bereiche ausgewählt.";
                StorageProgressBar.Value = 0;
                return;
            }

            bool en = Localization.CurrentLanguage == "en";
            bool confirmed = await ConfirmAsync(
                en ? "Delete selected categories?" : "Ausgewählte Bereiche löschen?",
                (en
                    ? $"{selected.Count} category/categories will be cleaned: {string.Join(", ", selected.Select(c => c.Name))}. This cannot be undone. Continue?"
                    : $"{selected.Count} Bereich(e) werden bereinigt: {string.Join(", ", selected.Select(c => c.Name))}. Das kann nicht rückgängig gemacht werden. Fortfahren?") +
                GetProtectedCleanupWarning(selected) +
                GetRunningProcessWarning(selected),
                respectDeleteConfirmationSetting: !RequiresProtectedCleanupConfirmation(selected));

            if (!confirmed) return;
            _isDeletingStorage = true;

            StorageRefreshButton.IsEnabled = false;
            StorageDeleteSelectedButton.IsEnabled = false;

            StorageProgressPanel.Visibility = Visibility.Visible;

            var normalCategories = selected.Where(c => !c.RequiresAdmin).ToList();
            var adminCategories = selected.Where(c => c.RequiresAdmin).ToList();

            StorageProgressBar.Maximum = normalCategories.Count + (adminCategories.Count > 0 ? 1 : 0);
            StorageProgressBar.Value = 0;

            var results = new List<string>();
            bool anySuccess = false;
            int step = 0;

            foreach (var category in normalCategories)
            {
                step++;
                StorageProgressText.Text = en
                    ? $"Deleting {category.Name} ({step}/{StorageProgressBar.Maximum})..."
                    : $"Lösche {category.Name} ({step}/{StorageProgressBar.Maximum})...";

                var (success, message) = await StorageService.DeleteCategoryAsync(category);
                results.Add(success ? $"{category.Name}: OK" : $"{category.Name}: {(en ? "Error" : "Fehler")}");
                Logger.Log($"Storage-Sammel-Löschung '{category.Name}': {(success ? "OK" : "Fehler")} - {message}");
                if (success) anySuccess = true;

                StorageProgressBar.Value = step;
            }

            if (adminCategories.Count > 0)
            {
                step++;
                StorageProgressText.Text = en
                    ? $"Deleting {adminCategories.Count} administrator category/categories... (approval required)"
                    : $"Lösche {adminCategories.Count} Admin-Bereich(e)... (Admin-Bestätigung nötig)";

                var exitCode = await RunElevatedStorageDeleteAsync(adminCategories);
                bool adminSuccess = exitCode == 0;

                foreach (var category in adminCategories)
                {
                    results.Add(adminSuccess ? $"{category.Name}: OK" : $"{category.Name}: {(en ? "Error" : "Fehler")}");
                    Logger.Log($"Storage-Sammel-Löschung (elevated) '{category.Name}': {(adminSuccess ? "OK" : $"Fehler (ExitCode {exitCode})")}");
                }

                if (adminSuccess) anySuccess = true;
                StorageProgressBar.Value = step;
            }

            StorageProgressText.Text = (en ? "Cleanup complete: " : "Bereinigung abgeschlossen: ") + string.Join(", ", results);

            bool anyFailure = results.Any(result =>
                result.Contains("Fehler", StringComparison.OrdinalIgnoreCase) ||
                result.Contains("Error", StringComparison.OrdinalIgnoreCase));
            if (anySuccess)
            {
                _settings.LastCleanupUtc = DateTime.UtcNow;
                _settings.Save();

                long totalFreedBytes = selected.Sum(c => c.SizeBytes);
                try
                {
                    var afterCleanup = await StorageService.GetCategoriesWithSizesAsync();
                    var afterByKey = afterCleanup.ToDictionary(category => category.Key, StringComparer.OrdinalIgnoreCase);
                    totalFreedBytes = selected.Sum(category =>
                        Math.Max(0, category.SizeBytes - (afterByKey.TryGetValue(category.Key, out var after) ? after.SizeBytes : 0)));
                }
                catch (Exception ex)
                {
                    Logger.LogError("Tatsächlich freigegebenen Speicher nachmessen", ex);
                }
                var freedDisplay = StorageService.FormatBytes(totalFreedBytes);
                LogActivity("\uE74D",
                    $"{selected.Count} Bereich(e) bereinigt ({freedDisplay})",
                    $"Cleaned {selected.Count} area(s) ({freedDisplay})",
                    anyFailure ? "Failed" : "Successful");
            }
            else
            {
                LogActivity("\uEA39",
                    "Bereinigung fehlgeschlagen",
                    "Cleanup failed",
                    "Failed");
            }

            await Task.Delay(2500);
            StorageProgressPanel.Visibility = Visibility.Collapsed;

            _isDeletingStorage = false;
            await LoadStorage();
        }
    }
}
