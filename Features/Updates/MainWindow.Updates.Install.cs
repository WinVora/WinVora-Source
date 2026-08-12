using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WinVora
{
    public sealed partial class MainWindow
    {
        private async void StartUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingWinget || _isLoadingWinget) return;
            _isUpdatingWinget = true;
            var selected = _wingetRows.Where(r => r.Toggle.IsOn).Select(r => r.Package).ToList();

            if (selected.Count == 0)
            {
                UpdateProgressPanel.Visibility = Visibility.Visible;
                UpdateProgressText.Text = "Keine Pakete ausgewählt.";
                UpdateProgressBar.Value = 0;
                _isUpdatingWinget = false;
                UpdateWingetSelectionButton();
                return;
            }

            bool en = Localization.CurrentLanguage == "en";
            bool containsEaApp = selected.Any(package =>
                package.Id.Equals("ElectronicArts.EADesktop", StringComparison.OrdinalIgnoreCase));
            var confirmation = CommonUiBuilder.CreateConfirmation(
                RootGrid.XamlRoot,
                en ? "Install selected updates?" : "Ausgewählte Updates installieren?",
                containsEaApp
                    ? (en
                        ? "The EA app is selected. Its installer previously restarted this PC without warning. WinVora will now open installers visibly, but a publisher installer may still request or initiate a restart. Save your work before continuing."
                        : "Die EA App ist ausgewählt. Ihr Installer hat diesen PC bereits ohne Warnung neu gestartet. WinVora öffnet Installer jetzt sichtbar, trotzdem kann ein Hersteller-Installer einen Neustart anfordern oder auslösen. Speichere vor dem Fortfahren deine Arbeit.")
                    : (en
                        ? "Publisher installers will be shown visibly. Some installers may request a restart. Save your work before continuing."
                        : "Die Installer der Hersteller werden sichtbar geöffnet. Einige Installer können einen Neustart verlangen. Speichere vor dem Fortfahren deine Arbeit."),
                en ? "Install" : "Installieren",
                en ? "Cancel" : "Abbrechen");

            if (await confirmation.ShowAsync() != ContentDialogResult.Primary)
            {
                _isUpdatingWinget = false;
                UpdateWingetSelectionButton();
                return;
            }

            RefreshButton.IsEnabled = false;
            StartUpdateButton.IsEnabled = false;
            CancelUpdateButton.Visibility = Visibility.Visible;
            CancelUpdateButton.IsEnabled = true;
            _wingetUpdateCancellation = new CancellationTokenSource();

            UpdateProgressPanel.Visibility = Visibility.Visible;
            UpdateProgressBar.Maximum = selected.Count;
            UpdateProgressBar.Value = 0;

            var results = new List<(WingetPackage Package, WingetUpdateResult Result)>();

            var progress = new Progress<WingetUpdateProgress>(p =>
            {
                string phaseText = p.Phase switch
                {
                    WingetUpdatePhase.Downloading => en ? "Downloading" : "Wird heruntergeladen",
                    WingetUpdatePhase.Installing => en ? "Installer is running" : "Installer läuft",
                    _ => en ? "Waiting for completion" : "Warte auf Abschluss"
                };
                CurrentPackageStatusText.Text = string.IsNullOrWhiteSpace(p.Text)
                    ? phaseText
                    : $"{phaseText} · {p.Text}" +
                      (string.IsNullOrWhiteSpace(p.Speed) ? "" : $" · {p.Speed}") +
                      (string.IsNullOrWhiteSpace(p.Eta) ? "" : $" · {(en ? "Remaining" : "Restzeit")} {p.Eta}");

                if (p.Percent.HasValue)
                {
                    CurrentPackageProgressBar.IsIndeterminate = false;
                    CurrentPackageProgressBar.Value = p.Percent.Value;
                }
                else
                {
                    CurrentPackageProgressBar.IsIndeterminate = true;
                }
            });

            for (int i = 0; i < selected.Count; i++)
            {
                var pkg = selected[i];
                SetGlobalStatus(Localization.CurrentLanguage == "en"
                    ? $"Updating {pkg.Name}..."
                    : $"{pkg.Name} wird aktualisiert...");
                UpdateProgressText.Text = $"Installiere {pkg.Name} ({i + 1}/{selected.Count})...";
                CurrentPackageStatusText.Text = "";
                CurrentPackageProgressBar.IsIndeterminate = true;
                CurrentPackageProgressBar.Value = 0;

                if (_wingetUpdateCancellation.IsCancellationRequested)
                    break;

                Logger.Log($"Programm-Update gestartet: {pkg.Name} [{pkg.Id}] {pkg.Version} -> {pkg.Available}");
                bool pendingRestartBefore = RestartDetectionService.IsRestartPending();
                var result = await _wingetUpdateService.UpgradeAsync(pkg.Id, progress, _wingetUpdateCancellation.Token);
                bool pendingRestartAfter = RestartDetectionService.IsRestartPending();
                if (!pendingRestartBefore && pendingRestartAfter && result.Status == WingetUpdateStatus.Successful)
                    result = result with
                    {
                        Status = WingetUpdateStatus.RestartRequired,
                        RestartRequired = true,
                        Message = en ? "Installed; Windows reports that a restart is required." : "Installiert; Windows meldet einen erforderlichen Neustart."
                    };

                results.Add((pkg, result));
                LogWingetUpdateActivity(pkg, result);
                Logger.Log($"Programm-Update beendet: {pkg.Name} [{pkg.Id}], Status={result.Status}, " +
                           $"ExitCode=0x{unchecked((uint)result.ExitCode):X8}, Meldung={result.Message}");

                CurrentPackageProgressBar.IsIndeterminate = false;
                CurrentPackageProgressBar.Value = 100;
                UpdateProgressBar.Value = i + 1;
            }

            bool cancelled = _wingetUpdateCancellation.IsCancellationRequested;
            int successCount = results.Count(item => item.Result.Status == WingetUpdateStatus.Successful);
            int failedCount = results.Count(item => item.Result.Status == WingetUpdateStatus.Failed);
            int cancelledCount = results.Count(item => item.Result.Status == WingetUpdateStatus.Cancelled) +
                                 Math.Max(0, selected.Count - results.Count);
            int restartCount = results.Count(item => item.Result.RestartRequired);
            UpdateProgressText.Text = cancelled
                ? (en ? "Update process cancelled." : "Updatevorgang abgebrochen.")
                : failedCount == 0
                    ? (en ? "All selected updates were installed." : "Alle ausgewählten Updates wurden installiert.")
                    : (en ? $"Finished with {failedCount} error(s)." : $"Mit {failedCount} Fehler(n) beendet.");
            CurrentPackageStatusText.Text = "";

            if (successCount > 0)
            {
                LogActivity("\uE895",
                    $"{successCount} Programm(e) aktualisiert",
                    $"{successCount} program(s) updated");
            }

            if (_settings.NotifyUpdateCompletion || (restartCount > 0 && _settings.NotifyRestartRequired))
            {
                NotificationService.ShowUpdateSummary(
                    successCount, failedCount, cancelledCount,
                    _settings.NotifyRestartRequired ? restartCount : 0);
            }

            await ShowUpdateSummaryAsync(results, selected.Count - results.Count);

            // Kurz die Abschlussmeldung stehen lassen, dann automatisch neu laden
            await Task.Delay(2000);
            UpdateProgressPanel.Visibility = Visibility.Collapsed;
            CancelUpdateButton.Visibility = Visibility.Collapsed;
            _wingetUpdateCancellation.Dispose();
            _wingetUpdateCancellation = null;
            SetGlobalStatus(null);

            // Nach einer Installation ist der Cache veraltet - erzwungener Reload.
            _cachedPackages = null;
            _isUpdatingWinget = false;
            await LoadWinget(forceRefresh: true);
        }

        private void CancelUpdate_Click(object sender, RoutedEventArgs e)
        {
            CancelUpdateButton.IsEnabled = false;
            CurrentPackageStatusText.Text = Localization.CurrentLanguage == "en"
                ? "Cancelling current installer..."
                : "Aktueller Installer wird abgebrochen...";
            _wingetUpdateCancellation?.Cancel();
            Logger.Log("Programm-Update wurde vom Benutzer abgebrochen.");
        }

        private void LogWingetUpdateActivity(WingetPackage package, WingetUpdateResult result)
        {
            string resultDe = result.Status switch
            {
                WingetUpdateStatus.Successful => "Erfolgreich",
                WingetUpdateStatus.RestartRequired => "Neustart erforderlich",
                WingetUpdateStatus.Cancelled => "Abgebrochen",
                _ => "Fehlgeschlagen"
            };
            string resultEn = result.Status switch
            {
                WingetUpdateStatus.Successful => "Successful",
                WingetUpdateStatus.RestartRequired => "Restart required",
                WingetUpdateStatus.Cancelled => "Cancelled",
                _ => "Failed"
            };

            _settings.ActivityLog.Insert(0, new ActivityLogEntry
            {
                TimestampUtc = DateTime.UtcNow,
                IconGlyph = result.Status is WingetUpdateStatus.Successful or WingetUpdateStatus.RestartRequired
                    ? "\uE895"
                    : "\uEA39",
                TextDe = $"{package.Name}: {resultDe}",
                TextEn = $"{package.Name}: {resultEn}",
                PackageId = package.Id,
                OldVersion = package.Version,
                NewVersion = package.Available,
                Result = result.Status.ToString(),
                ExitCode = result.ExitCode
            });

            while (_settings.ActivityLog.Count > 20)
                _settings.ActivityLog.RemoveAt(_settings.ActivityLog.Count - 1);
            _settings.Save();
        }

        private async Task ShowUpdateSummaryAsync(
            List<(WingetPackage Package, WingetUpdateResult Result)> results,
            int notStartedCount)
        {
            bool en = Localization.CurrentLanguage == "en";
            var panel = new StackPanel { Spacing = 10, MaxWidth = 560 };

            foreach (var item in results)
            {
                string symbol = item.Result.Status switch
                {
                    WingetUpdateStatus.Successful => "✓",
                    WingetUpdateStatus.RestartRequired => "↻",
                    WingetUpdateStatus.Cancelled => "■",
                    _ => "!"
                };
                Windows.UI.Color statusColor = item.Result.Status switch
                {
                    WingetUpdateStatus.Successful => Windows.UI.Color.FromArgb(0xFF, 0x4C, 0xD9, 0x73),
                    WingetUpdateStatus.RestartRequired => Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xC1, 0x4D),
                    WingetUpdateStatus.Cancelled => Windows.UI.Color.FromArgb(0xFF, 0xB0, 0xB0, 0xB0),
                    _ => Windows.UI.Color.FromArgb(0xFF, 0xFF, 0x6B, 0x6B)
                };
                panel.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(10),
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    BorderBrush = new SolidColorBrush(statusColor),
                    Background = (SolidColorBrush)RootGrid.Resources["AppOverlay18"],
                    Padding = new Thickness(12, 10, 12, 10),
                    Child = new TextBlock
                    {
                        Text = $"{symbol}  {item.Package.Name}  ·  {item.Package.Version} → {item.Package.Available}\n{item.Result.Message}",
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = (SolidColorBrush)RootGrid.Resources["AppForegroundBrush"]
                    }
                });
            }

            if (notStartedCount > 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = en ? $"■  {notStartedCount} update(s) were not started." : $"■  {notStartedCount} Update(s) wurden nicht gestartet.",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = (SolidColorBrush)RootGrid.Resources["AppFaintForegroundBrush"]
                });
            }

            var dialog = CommonUiBuilder.CreateConfirmation(
                RootGrid.XamlRoot,
                en ? "Update summary" : "Update-Abschlussbericht",
                new ScrollViewer { Content = panel, MaxHeight = 430 },
                results.Any(item => item.Result.Status == WingetUpdateStatus.Failed)
                    ? (en ? "Retry failed" : "Fehlgeschlagene erneut versuchen")
                    : null,
                en ? "Close" : "Schließen");
            var choice = await dialog.ShowAsync();
            if (choice == ContentDialogResult.Primary)
            {
                var retryResults = new List<(WingetPackage Package, WingetUpdateResult Result)>();
                foreach (var failed in results.Where(item => item.Result.Status == WingetUpdateStatus.Failed))
                {
                    SetGlobalStatus(en ? $"Retrying {failed.Package.Name}..." : $"{failed.Package.Name} wird erneut versucht...");
                    var retry = await _wingetUpdateService.UpgradeAsync(
                        failed.Package.Id,
                        new Progress<WingetUpdateProgress>(_ => { }),
                        CancellationToken.None);
                    retryResults.Add((failed.Package, retry));
                    LogWingetUpdateActivity(failed.Package, retry);
                }
                SetGlobalStatus(null);
                await ShowUpdateSummaryAsync(retryResults, 0);
            }
        }

        // Ermittelt die Spaltenstart-Positionen sprachunabhängig:
        // eine neue Spalte beginnt dort, wo nach 2+ Leerzeichen wieder
        // ein Nicht-Leerzeichen folgt.
        private int[] GetColumnStarts(string header)
        {
            var starts = new List<int> { 0 };
            for (int i = 2; i < header.Length; i++)
            {
                if (header[i] != ' ' && header[i - 1] == ' ' && header[i - 2] == ' ')
                {
                    starts.Add(i);
                }
            }
            return starts.ToArray();
        }

        private WingetPackage? Parse(string line, int[]? columns = null)
        {
            columns ??= _wingetColumns;
            return WingetTableParser.Parse(line, columns);
        }

        private void WingetClearSearch_Click(object sender, RoutedEventArgs e)
        {
            WingetSearchBox.Text = "";
            WingetSearchBox.Focus(FocusState.Programmatic);
        }
    }
}
