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
            var selected = _wingetRows.Where(r => r.Toggle.IsChecked == true).Select(r => r.Package).ToList();

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
                SetWingetCardStatus(pkg.Id, en ? "Installing" : "Wird installiert", "AppWarningBrush");
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
                SetWingetCardStatus(pkg.Id,
                    result.Status switch
                    {
                        WingetUpdateStatus.Successful => en ? "Installed" : "Installiert",
                        WingetUpdateStatus.RestartRequired => en ? "Restart required" : "Neustart erforderlich",
                        WingetUpdateStatus.Cancelled => en ? "Cancelled" : "Abgebrochen",
                        _ => en ? "Failed" : "Fehlgeschlagen"
                    },
                    result.Status switch
                    {
                        WingetUpdateStatus.Successful => "AppSuccessBrush",
                        WingetUpdateStatus.RestartRequired => "AppWarningBrush",
                        WingetUpdateStatus.Cancelled => "AppNeutralStatusBrush",
                        _ => "AppErrorBrush"
                    });
                Logger.Log($"Programm-Update beendet: {pkg.Name} [{pkg.Id}], Status={result.Status}, " +
                           $"ExitCode=0x{unchecked((uint)result.ExitCode):X8}, Meldung={result.Message}");

                CurrentPackageProgressBar.IsIndeterminate = false;
                CurrentPackageProgressBar.Value = 100;
                UpdateProgressBar.Value = i + 1;
            }

            bool cancelled = _wingetUpdateCancellation.IsCancellationRequested;
            if (!cancelled)
            {
                CurrentPackageStatusText.Text = en
                    ? "Verifying installed versions..."
                    : "Installierte Versionen werden überprüft...";
                results = await VerifyUpdateResultsAsync(results, en, _wingetUpdateCancellation.Token);
            }

            foreach (var item in results)
                LogWingetUpdateActivity(item.Package, item.Result);

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

            // Die Seite bereits vor dem Bericht mit dem geprüften Stand
            // aktualisieren. So zeigt der Hintergrund nicht weiter die alte
            // Auswahl und ein Exitcode 0 wird nicht blind als Erfolg gewertet.
            if (_cachedPackages != null)
            {
                RenderWingetPackages(_cachedPackages);
                RestoreVerifiedCardStatuses(results, en);
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

        private void SetWingetCardStatus(string packageId, string text, string brushKey)
        {
            if (!_wingetStatusBadges.TryGetValue(packageId, out var badge)) return;
            badge.Text = text;
            if (RootGrid.Resources[brushKey] is SolidColorBrush brush)
                badge.Foreground = brush;
        }

        private void RestoreVerifiedCardStatuses(
            IEnumerable<(WingetPackage Package, WingetUpdateResult Result)> results,
            bool english)
        {
            foreach (var item in results)
            {
                switch (item.Result.Status)
                {
                    case WingetUpdateStatus.Failed:
                        SetWingetCardStatus(item.Package.Id, english ? "Failed" : "Fehlgeschlagen", "AppErrorBrush");
                        break;
                    case WingetUpdateStatus.RestartRequired:
                        SetWingetCardStatus(item.Package.Id, english ? "Restart required" : "Neustart erforderlich", "AppWarningBrush");
                        break;
                    case WingetUpdateStatus.Cancelled:
                        SetWingetCardStatus(item.Package.Id, english ? "Cancelled" : "Abgebrochen", "AppNeutralStatusBrush");
                        break;
                }
            }
        }

        private async Task<List<(WingetPackage Package, WingetUpdateResult Result)>> VerifyUpdateResultsAsync(
            List<(WingetPackage Package, WingetUpdateResult Result)> results,
            bool english,
            CancellationToken cancellationToken)
        {
            try
            {
                // Manche Hersteller-Installer kehren mit 0 zurück, obwohl sie
                // nur gestartet wurden oder die installierte Version nicht
                // geändert haben. Eine frische WinGet-Abfrage ist deshalb die
                // verlässliche Abschlusskontrolle.
                await Task.Delay(1200, cancellationToken);
                var discovery = await WingetDiscoveryService.GetUpgradesAsync(cancellationToken);
                _cachedPackages = discovery.Packages;
                _wingetColumns = discovery.Columns;

                for (int index = 0; index < results.Count; index++)
                {
                    var item = results[index];
                    if (item.Result.Status is not (WingetUpdateStatus.Successful or WingetUpdateStatus.RestartRequired))
                        continue;

                    var stillAvailable = discovery.Packages.FirstOrDefault(package =>
                        package.Id.Equals(item.Package.Id, StringComparison.OrdinalIgnoreCase));
                    if (stillAvailable == null) continue;

                    results[index] = (item.Package, item.Result with
                    {
                        Status = WingetUpdateStatus.Failed,
                        RestartRequired = false,
                        Message = english
                            ? "The installer reported success, but WinGet still offers this update. The installed version was not changed."
                            : "Der Installer meldete Erfolg, aber WinGet bietet dieses Update weiterhin an. Die installierte Version wurde nicht geändert."
                    });
                    Logger.Log($"Update-Nachprüfung fehlgeschlagen: {item.Package.Name} [{item.Package.Id}] wird weiterhin angeboten.");
                }
            }
            catch (OperationCanceledException)
            {
                // Erwarteter Abbruch beim Schließen oder manuellen Abbrechen.
            }
            catch (Exception ex)
            {
                // Die Nachprüfung verbessert die Aussagekraft, darf aber einen
                // bereits abgeschlossenen Installationslauf nicht verdecken.
                Logger.LogError("Update-Nachprüfung", ex);
                _cachedPackages = null;
            }

            return results;
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
            int successful = results.Count(item => item.Result.Status == WingetUpdateStatus.Successful);
            int failed = results.Count(item => item.Result.Status == WingetUpdateStatus.Failed);
            int cancelled = results.Count(item => item.Result.Status == WingetUpdateStatus.Cancelled) + notStartedCount;
            int restartRequired = results.Count(item => item.Result.Status == WingetUpdateStatus.RestartRequired);

            var summaryLine = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 14,
                Margin = new Thickness(0, 0, 0, 4)
            };
            void AddSummary(string text, string brushKey)
            {
                summaryLine.Children.Add(new TextBlock
                {
                    Text = text,
                    FontSize = 13,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = (SolidColorBrush)RootGrid.Resources[brushKey]
                });
            }
            AddSummary(en ? $"{successful} successful" : $"{successful} erfolgreich", "AppSuccessBrush");
            if (failed > 0) AddSummary(en ? $"{failed} failed" : $"{failed} fehlgeschlagen", "AppErrorBrush");
            if (restartRequired > 0) AddSummary(en ? $"{restartRequired} restart" : $"{restartRequired} Neustart", "AppWarningBrush");
            if (cancelled > 0) AddSummary(en ? $"{cancelled} cancelled" : $"{cancelled} abgebrochen", "AppNeutralStatusBrush");
            panel.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 10, 12, 10),
                Background = (SolidColorBrush)RootGrid.Resources["AppOverlay18"],
                Child = summaryLine
            });

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

            var technicalDetails = results
                .Where(item => item.Result.ExitCode != 0)
                .Select(item => $"{item.Package.Name}: 0x{unchecked((uint)item.Result.ExitCode):X8}")
                .ToList();
            if (technicalDetails.Count > 0)
            {
                panel.Children.Add(new Expander
                {
                    Header = en ? "Technical details" : "Technische Details",
                    IsExpanded = false,
                    Content = new TextBlock
                    {
                        Text = string.Join("\n", technicalDetails),
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = 12,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = (SolidColorBrush)RootGrid.Resources["AppFaintForegroundBrush"]
                    }
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
                foreach (var failedItem in results.Where(item => item.Result.Status == WingetUpdateStatus.Failed))
                {
                    SetGlobalStatus(en ? $"Retrying {failedItem.Package.Name}..." : $"{failedItem.Package.Name} wird erneut versucht...");
                    var retry = await _wingetUpdateService.UpgradeAsync(
                        failedItem.Package.Id,
                        new Progress<WingetUpdateProgress>(_ => { }),
                        CancellationToken.None);
                    retryResults.Add((failedItem.Package, retry));
                }
                retryResults = await VerifyUpdateResultsAsync(retryResults, en, CancellationToken.None);
                foreach (var retryItem in retryResults)
                    LogWingetUpdateActivity(retryItem.Package, retryItem.Result);
                if (_cachedPackages != null)
                {
                    RenderWingetPackages(_cachedPackages);
                    RestoreVerifiedCardStatuses(retryResults, en);
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
