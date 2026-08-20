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

        private static Task<int> RunElevatedStorageDeleteAsync(List<StorageCategory> categories) =>
            ElevatedActionService.RunStorageDeleteElevatedAsync(categories.Select(category => category.Key));

        private async void StorageDeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            if (_isDeletingStorage || _isLoadingStorage) return;

            try
            {
                await StorageDeleteSelectedCoreAsync();
            }
            catch (OperationCanceledException)
            {
                ShowInfo(Localization.CurrentLanguage == "en"
                        ? "The cleanup was cancelled."
                        : "Die Bereinigung wurde abgebrochen.",
                    InfoBarSeverity.Warning);
            }
            catch (Exception ex)
            {
                Logger.LogError("Speicherbereinigung", ex);
                ShowInfo(Localization.CurrentLanguage == "en"
                        ? "Cleanup ended unexpectedly. WinVora has reset the cleanup controls."
                        : "Die Bereinigung wurde unerwartet beendet. WinVora hat die Steuerung zurückgesetzt.",
                    InfoBarSeverity.Error);
            }
            finally
            {
                _storageOperations.CompleteDelete();
                try
                {
                    StorageRefreshButton.IsEnabled = true;
                    StorageProgressBar.IsIndeterminate = false;
                    UpdateStorageSelectionSummary();
                }
                catch (Exception ex)
                {
                    Logger.LogErrorOnce("Storage-Oberfläche zurücksetzen", ex);
                }
            }
        }

        private async Task StorageDeleteSelectedCoreAsync()
        {
            if (_isDeletingStorage || _isLoadingStorage) return;
            var selected = _storageRows.Where(r => _viewState.IsStorageSelected(r.Category.Key)).Select(r => r.Category).ToList();

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
            if (!_storageOperations.TryBeginDelete()) return;

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

                var (success, message) = await StorageService.DeleteCategoryAsync(
                    category,
                    _storageOperations.Token);
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
                    var afterCleanup = await StorageService.GetCategoriesWithSizesAsync(_storageOperations.Token);
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

            _storageOperations.CompleteDelete();
            await LoadStorage();
        }
    }
}
