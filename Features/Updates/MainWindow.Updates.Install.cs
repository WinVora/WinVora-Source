using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;

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
                UpdateProgressText.Text = Localization.CurrentLanguage == "en" ? "No packages selected." : "Keine Pakete ausgewählt.";
                UpdateProgressBar.Value = 0;
                _isUpdatingWinget = false;
                UpdateWingetSelectionButton();
                return;
            }

            bool en = Localization.CurrentLanguage == "en";
            var battery = await Task.Run(UpdatePowerGuard.ReadBatteryState);
            if (battery.HasBattery && battery.ChargePercent <= 20 && !battery.Charging)
            {
                var batteryWarning = CommonUiBuilder.CreateConfirmation(
                    RootGrid.XamlRoot,
                    en ? "Low battery" : "Niedriger Akkustand",
                    en
                        ? $"The battery is at {battery.ChargePercent}%. Connect the PC to power before installing updates."
                        : $"Der Akkustand liegt bei {battery.ChargePercent} %. Schließe den PC vor der Updateinstallation an das Stromnetz an.",
                    en ? "Continue anyway" : "Trotzdem fortfahren",
                    en ? "Cancel" : "Abbrechen");
                if (await batteryWarning.ShowAsync() != ContentDialogResult.Primary)
                {
                    _isUpdatingWinget = false;
                    UpdateWingetSelectionButton();
                    return;
                }
            }
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

            var confirmationFocus = FocusManager.GetFocusedElement(RootGrid.XamlRoot) as Control;
            var confirmationResult = await confirmation.ShowAsync();
            confirmationFocus?.Focus(FocusState.Programmatic);
            if (confirmationResult != ContentDialogResult.Primary)
            {
                _isUpdatingWinget = false;
                UpdateWingetSelectionButton();
                return;
            }

            using var updatePowerGuard = new UpdatePowerGuard();
            updatePowerGuard.Start();

            RefreshButton.IsEnabled = false;
            StartUpdateButton.IsEnabled = false;
            CancelUpdateButton.Visibility = Visibility.Visible;
            CancelUpdateButton.IsEnabled = true;
            _wingetUpdateCancellation = new CancellationTokenSource();

            UpdateProgressPanel.Visibility = Visibility.Visible;
            UpdateProgressBar.Maximum = selected.Count;
            UpdateProgressBar.Value = 0;
            if (RootGrid.Resources["AppAccentBrush"] is SolidColorBrush progressAccent)
                UpdateProgressBar.Foreground = progressAccent;

            var results = new List<(WingetPackage Package, WingetUpdateResult Result)>();
            string? activePackageId = null;

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

                if (activePackageId != null)
                    SetWingetCardStatus(activePackageId, phaseText, "AppWarningBrush");

                if (p.Percent.HasValue)
                {
                    CurrentPackageProgressBar.IsIndeterminate = false;
                    CurrentPackageProgressBar.Value = p.Percent.Value;
                }
                else
                {
                    CurrentPackageProgressBar.IsIndeterminate = true;
                }
                if (activePackageId != null)
                    SetWingetCardProgress(activePackageId, p.Percent, visible: true);
            });

            for (int i = 0; i < selected.Count; i++)
            {
                var pkg = selected[i];
                activePackageId = pkg.Id;
                SetWingetCardStatus(pkg.Id, en ? "Installing" : "Wird installiert", "AppWarningBrush");
                SetWingetCardProgress(pkg.Id, null, visible: true);
                SetWingetCardProgressColor(pkg.Id, "AppAccentBrush");
                SetGlobalStatus(Localization.CurrentLanguage == "en"
                    ? $"Updating {pkg.Name}..."
                    : $"{pkg.Name} wird aktualisiert...");
                UpdateProgressText.Text = en
                    ? $"Installing {pkg.Name} ({i + 1}/{selected.Count})..."
                    : $"Installiere {pkg.Name} ({i + 1}/{selected.Count})...";
                CurrentPackageStatusText.Text = "";
                CurrentPackageProgressBar.IsIndeterminate = true;
                CurrentPackageProgressBar.Value = 0;

                if (_wingetUpdateCancellation.IsCancellationRequested)
                    break;

                Logger.Log($"Programm-Update gestartet: {pkg.Name} [{pkg.Id}] {pkg.Version} -> {pkg.Available}");
                bool pendingRestartBefore = RestartDetectionService.IsRestartPending();
                var result = await UpgradeWithElevationAsync(
                    pkg.Id,
                    pkg.Name,
                    progress,
                    _wingetUpdateCancellation.Token,
                    en);
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
                        WingetUpdateStatus.Successful => en ? "✓ Installed" : "✓ Installiert",
                        WingetUpdateStatus.RestartRequired => en ? "Restart required" : "Neustart erforderlich",
                        WingetUpdateStatus.Cancelled => en ? "Cancelled" : "Abgebrochen",
                        WingetUpdateStatus.Unverified => en ? "Not confirmed" : "Nicht bestätigt",
                        _ => en ? "Failed" : "Fehlgeschlagen"
                    },
                    result.Status switch
                    {
                        WingetUpdateStatus.Successful => "AppSuccessBrush",
                        WingetUpdateStatus.RestartRequired => "AppWarningBrush",
                        WingetUpdateStatus.Cancelled => "AppNeutralStatusBrush",
                        WingetUpdateStatus.Unverified => "AppWarningBrush",
                        _ => "AppErrorBrush"
                    });
                Logger.Log($"Programm-Update beendet: {pkg.Name} [{pkg.Id}], Status={result.Status}, " +
                           $"ExitCode=0x{unchecked((uint)result.ExitCode):X8}, Meldung={result.Message}");

                CurrentPackageProgressBar.IsIndeterminate = false;
                CurrentPackageProgressBar.Value = 100;
                SetWingetCardProgress(pkg.Id, 100, visible: true);
                SetWingetCardProgressColor(pkg.Id, result.Status switch
                {
                    WingetUpdateStatus.Successful or WingetUpdateStatus.RestartRequired => "AppSuccessBrush",
                    WingetUpdateStatus.Failed => "AppErrorBrush",
                    WingetUpdateStatus.Cancelled => "AppNeutralStatusBrush",
                    _ => "AppWarningBrush"
                });
                UpdateProgressBar.Value = i + 1;
            }
            activePackageId = null;

            bool cancelled = _wingetUpdateCancellation.IsCancellationRequested;
            if (!cancelled)
            {
                CurrentPackageStatusText.Text = en
                    ? "Verifying installed versions..."
                    : "Installierte Versionen werden überprüft...";
                foreach (var item in results.Where(item =>
                             item.Result.Status is WingetUpdateStatus.Successful or WingetUpdateStatus.RestartRequired))
                {
                    SetWingetCardStatus(item.Package.Id, en ? "Verifying" : "Wird geprüft", "AppWarningBrush");
                    SetWingetCardProgress(item.Package.Id, null, visible: true);
                    SetWingetCardProgressColor(item.Package.Id, "AppAccentBrush");
                }
                results = await VerifyUpdateResultsAsync(results, en, _wingetUpdateCancellation.Token);
            }

            foreach (var item in results)
                LogWingetUpdateActivity(item.Package, item.Result);

            int successCount = results.Count(item => item.Result.Status == WingetUpdateStatus.Successful);
            int failedCount = results.Count(item => item.Result.Status == WingetUpdateStatus.Failed);
            int cancelledCount = results.Count(item => item.Result.Status == WingetUpdateStatus.Cancelled) +
                                 Math.Max(0, selected.Count - results.Count);
            int restartCount = results.Count(item => item.Result.RestartRequired);
            int unverifiedCount = results.Count(item => item.Result.Status == WingetUpdateStatus.Unverified);
            LogUpdateSession(results, selected.Count - results.Count, en);
            UpdateProgressText.Text = cancelled
                ? (en ? "Update process cancelled." : "Updatevorgang abgebrochen.")
                : failedCount == 0
                    ? (en ? "All selected updates were installed." : "Alle ausgewählten Updates wurden installiert.")
                    : (en ? $"Finished with {failedCount} error(s)." : $"Mit {failedCount} Fehler(n) beendet.");
            CurrentPackageStatusText.Text = "";
            string overallProgressBrush = cancelled
                ? "AppNeutralStatusBrush"
                : failedCount > 0
                    ? "AppErrorBrush"
                    : unverifiedCount > 0 || restartCount > 0
                        ? "AppWarningBrush"
                        : "AppSuccessBrush";
            if (RootGrid.Resources[overallProgressBrush] is SolidColorBrush overallBrush)
                UpdateProgressBar.Foreground = overallBrush;

            if (successCount > 0)
            {
                LogActivity("\uE895",
                    $"{successCount} Programm(e) aktualisiert",
                    $"{successCount} program(s) updated");
            }

            if (_settings.NotifyUpdateCompletion || (restartCount > 0 && _settings.NotifyRestartRequired))
            {
                bool notificationShown = NotificationService.ShowUpdateSummary(
                    successCount, failedCount, cancelledCount,
                    _settings.NotifyRestartRequired ? restartCount : 0,
                    unverifiedCount);
                if (!notificationShown)
                {
                    ShowInfo(Localization.CurrentLanguage == "en"
                            ? "Updates finished. The Windows notification was unavailable, so this message is shown in WinVora."
                            : "Updates abgeschlossen. Die Windows-Benachrichtigung war nicht verfügbar; deshalb erscheint diese Meldung in WinVora.",
                        failedCount > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success);
                }
            }

            // Die Seite bereits vor dem Bericht mit dem geprüften Stand
            // aktualisieren. So zeigt der Hintergrund nicht weiter die alte
            // Auswahl und ein Exitcode 0 wird nicht blind als Erfolg gewertet.
            if (_cachedPackages != null)
            {
                RenderWingetPackagesPreservingState(_cachedPackages);
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
                    case WingetUpdateStatus.Unverified:
                        SetWingetCardStatus(item.Package.Id, english ? "Not confirmed" : "Nicht bestätigt", "AppWarningBrush");
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
                // Hersteller-Installer und die WinGet-Quelle benötigen nach dem
                // Prozessende gelegentlich einige Sekunden, bis die neue Version
                // sichtbar ist. Mehrere kurze Prüfungen verhindern falsche
                // Fehlermeldungen und veraltete Karten.
                WingetDiscoveryResult? discovery = null;
                bool needsExtendedVerification = results.Any(item =>
                    item.Result.Status is WingetUpdateStatus.Successful or WingetUpdateStatus.RestartRequired &&
                    WingetUpdateVerifier.NeedsExtendedVerification(item.Package));
                int[] verificationDelaysMs = needsExtendedVerification
                    ? new[] { 1200, 2200, 3500, 5000 }
                    : new[] { 1200, 2200, 3500 };
                foreach (int delayMs in verificationDelaysMs)
                {
                    await Task.Delay(delayMs, cancellationToken);
                    discovery = await WingetDiscoveryService.GetUpgradesAsync(cancellationToken);

                    bool unchangedSuccessfulPackageRemains = results.Any(item =>
                        item.Result.Status is WingetUpdateStatus.Successful or WingetUpdateStatus.RestartRequired &&
                        WingetUpdateVerifier.IsStillUnchanged(item.Package, discovery.Packages));
                    if (!unchangedSuccessfulPackageRemains)
                        break;

                    Logger.Log($"Update-Nachprüfung wartet weiter: Versuch mit {delayMs} ms abgeschlossen.");
                }

                if (discovery == null)
                    return results;

                _cachedPackages = discovery.Packages;
                _wingetColumns = discovery.Columns;

                for (int index = 0; index < results.Count; index++)
                {
                    var item = results[index];
                    if (item.Result.Status is not (WingetUpdateStatus.Successful or WingetUpdateStatus.RestartRequired))
                        continue;

                    if (!WingetUpdateVerifier.IsStillUnchanged(item.Package, discovery.Packages))
                        continue;

                    results[index] = (item.Package, item.Result with
                    {
                        Status = WingetUpdateStatus.Unverified,
                        RestartRequired = false,
                        Message = english
                            ? "The installer completed successfully, but WinGet still compares different version values. The result could not be confirmed; repeated installation is not recommended."
                            : "Der Installer wurde erfolgreich beendet, aber WinGet vergleicht weiterhin unterschiedliche Versionswerte. Das Ergebnis konnte nicht bestätigt werden; eine wiederholte Installation wird nicht empfohlen."
                    });
                    Logger.Log($"Update-Nachprüfung nicht eindeutig: {item.Package.Name} [{item.Package.Id}] wird weiterhin mit unveränderter installierter Version angeboten.");
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
                WingetUpdateStatus.Unverified => "Nicht bestätigt",
                _ => "Fehlgeschlagen"
            };
            string resultEn = result.Status switch
            {
                WingetUpdateStatus.Successful => "Successful",
                WingetUpdateStatus.RestartRequired => "Restart required",
                WingetUpdateStatus.Cancelled => "Cancelled",
                WingetUpdateStatus.Unverified => "Not confirmed",
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

        private void LogUpdateSession(
            IReadOnlyCollection<(WingetPackage Package, WingetUpdateResult Result)> results,
            int notStartedCount,
            bool english)
        {
            if (results.Count == 0 && notStartedCount == 0) return;
            string sessionId = Guid.NewGuid().ToString("N");
            int successful = results.Count(item => item.Result.Status == WingetUpdateStatus.Successful);
            int failed = results.Count(item => item.Result.Status == WingetUpdateStatus.Failed);
            int cancelled = results.Count(item => item.Result.Status == WingetUpdateStatus.Cancelled) + notStartedCount;
            int restart = results.Count(item => item.Result.Status == WingetUpdateStatus.RestartRequired);
            int unverified = results.Count(item => item.Result.Status == WingetUpdateStatus.Unverified);
            string detailLines = string.Join("\n", results.Select(item =>
                $"{item.Package.Name}: {item.Package.Version} → {item.Package.Available} · {item.Result.Status} · {item.Result.Message}"));
            string summaryDe = $"{successful} erfolgreich · {failed} fehlgeschlagen · {unverified} nicht bestätigt · {cancelled} abgebrochen · {restart} Neustart";
            string summaryEn = $"{successful} successful · {failed} failed · {unverified} not confirmed · {cancelled} cancelled · {restart} restart";

            _settings.ActivityLog.Insert(0, new ActivityLogEntry
            {
                TimestampUtc = DateTime.UtcNow,
                IconGlyph = "\uE895",
                TextDe = "Update-Sitzung abgeschlossen",
                TextEn = "Update session completed",
                Result = failed > 0 ? "Failed" : cancelled > 0 ? "Cancelled" : restart > 0 ? "RestartRequired" : "Successful",
                SessionId = sessionId,
                DetailsDe = summaryDe + "\n" + detailLines,
                DetailsEn = summaryEn + "\n" + detailLines
            });
            while (_settings.ActivityLog.Count > 100)
                _settings.ActivityLog.RemoveAt(_settings.ActivityLog.Count - 1);
            _settings.Save();
        }

        private async Task ShowUpdateSummaryAsync(
            List<(WingetPackage Package, WingetUpdateResult Result)> results,
            int notStartedCount)
        {
            bool en = Localization.CurrentLanguage == "en";
            var panel = new StackPanel { Spacing = 10, MaxWidth = 560 };
            ContentDialog? dialog = null;
            int successful = results.Count(item => item.Result.Status == WingetUpdateStatus.Successful);
            int failed = results.Count(item => item.Result.Status == WingetUpdateStatus.Failed);
            int cancelled = results.Count(item => item.Result.Status == WingetUpdateStatus.Cancelled) + notStartedCount;
            int restartRequired = results.Count(item => item.Result.Status == WingetUpdateStatus.RestartRequired);
            int unverified = results.Count(item => item.Result.Status == WingetUpdateStatus.Unverified);

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
            if (unverified > 0) AddSummary(en ? $"{unverified} not confirmed" : $"{unverified} nicht bestätigt", "AppWarningBrush");
            if (cancelled > 0) AddSummary(en ? $"{cancelled} cancelled" : $"{cancelled} abgebrochen", "AppNeutralStatusBrush");
            panel.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 10, 12, 10),
                Background = (SolidColorBrush)RootGrid.Resources["AppOverlay18"],
                Child = summaryLine
            });
            if (unverified > 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = en
                        ? "Not confirmed means the installer finished, but WinGet still reports the previous version. WinVora does not repeat the installation automatically."
                        : "Nicht bestätigt bedeutet: Der Installer wurde beendet, WinGet meldet aber weiterhin die vorherige Version. WinVora wiederholt die Installation nicht automatisch.",
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    Foreground = (SolidColorBrush)RootGrid.Resources["AppFaintForegroundBrush"]
                });
            }
            var restartNames = results
                .Where(item => item.Result.Status == WingetUpdateStatus.RestartRequired)
                .Select(item => item.Package.Name)
                .ToList();
            if (restartNames.Count > 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = (en ? "Restart required: " : "Neustart erforderlich: ") + string.Join(", ", restartNames),
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = (SolidColorBrush)RootGrid.Resources["AppWarningBrush"]
                });
            }

            foreach (var item in results)
            {
                string symbol = item.Result.Status switch
                {
                    WingetUpdateStatus.Successful => "✓",
                    WingetUpdateStatus.RestartRequired => "↻",
                    WingetUpdateStatus.Cancelled => "■",
                    WingetUpdateStatus.Unverified => "?",
                    _ => "!"
                };
                Windows.UI.Color statusColor = item.Result.Status switch
                {
                    WingetUpdateStatus.Successful => Windows.UI.Color.FromArgb(0xFF, 0x4C, 0xD9, 0x73),
                    WingetUpdateStatus.RestartRequired => Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xC1, 0x4D),
                    WingetUpdateStatus.Cancelled => Windows.UI.Color.FromArgb(0xFF, 0xB0, 0xB0, 0xB0),
                    WingetUpdateStatus.Unverified => Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xC1, 0x4D),
                    _ => Windows.UI.Color.FromArgb(0xFF, 0xFF, 0x6B, 0x6B)
                };
                var resultContent = new StackPanel { Spacing = 8 };
                resultContent.Children.Add(new TextBlock
                {
                    Text = $"{symbol}  {item.Package.Name}  ·  {item.Package.Version} → {item.Package.Available}\n{item.Result.Message}",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = (SolidColorBrush)RootGrid.Resources["AppForegroundBrush"]
                });
                if (item.Result.Status == WingetUpdateStatus.Failed)
                {
                    string recommendation = GetUpdateRecommendation(item.Result, en);
                    resultContent.Children.Add(new TextBlock
                    {
                        Text = (en ? "Recommended: " : "Empfehlung: ") + recommendation,
                        TextWrapping = TextWrapping.Wrap,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Foreground = (SolidColorBrush)RootGrid.Resources["AppWarningBrush"]
                    });
                }
                if (item.Result.Status == WingetUpdateStatus.Failed)
                {
                    var retryButton = new Button
                    {
                        Content = en ? "Retry this update" : "Dieses Update erneut versuchen",
                        HorizontalAlignment = HorizontalAlignment.Left
                    };
                    retryButton.Click += async (_, __) =>
                    {
                        retryButton.IsEnabled = false;
                        dialog?.Hide();
                        var retryResults = await RetryUpdatesAsync(new[] { item }, en, CancellationToken.None);
                        await ShowUpdateSummaryAsync(retryResults, 0);
                    };
                    resultContent.Children.Add(retryButton);
                    var rollbackButton = new Button
                    {
                        Content = en ? "Open repair / rollback options" : "Reparatur-/Rollback-Optionen öffnen",
                        HorizontalAlignment = HorizontalAlignment.Left
                    };
                    rollbackButton.Click += async (_, __) =>
                    {
                        await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:appsfeatures"));
                    };
                    ToolTipService.SetToolTip(rollbackButton, en
                        ? "Opens Windows Installed apps. Select the affected app for repair, uninstall or rollback options."
                        : "Öffnet die installierten Apps in Windows. Wähle dort das betroffene Programm für Reparatur, Deinstallation oder Rollback.");
                    resultContent.Children.Add(rollbackButton);
                }
                else if (item.Result.Status == WingetUpdateStatus.Unverified)
                {
                    var verifyButton = new Button
                    {
                        Content = en ? "Check version again" : "Version erneut prüfen",
                        HorizontalAlignment = HorizontalAlignment.Left
                    };
                    verifyButton.Click += async (_, __) =>
                    {
                        verifyButton.IsEnabled = false;
                        verifyButton.Content = en ? "Checking..." : "Wird geprüft...";
                        try
                        {
                            var discovery = await WingetDiscoveryService.GetUpgradesAsync(CancellationToken.None);
                            bool unchanged = WingetUpdateVerifier.IsStillUnchanged(item.Package, discovery.Packages);
                            _cachedPackages = discovery.Packages;
                            _wingetColumns = discovery.Columns;
                            verifyButton.Content = unchanged
                                ? (en ? "Still not confirmed" : "Weiterhin nicht bestätigt")
                                : (en ? "✓ Version confirmed" : "✓ Version bestätigt");
                            if (!unchanged)
                                SetWingetCardStatus(item.Package.Id, en ? "✓ Installed" : "✓ Installiert", "AppSuccessBrush");
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError("Installierte Version erneut prüfen", ex);
                            verifyButton.Content = en ? "Check failed" : "Prüfung fehlgeschlagen";
                        }
                    };
                    resultContent.Children.Add(verifyButton);
                }

                panel.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(10),
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    BorderBrush = new SolidColorBrush(statusColor),
                    Background = (SolidColorBrush)RootGrid.Resources["AppOverlay18"],
                    Padding = new Thickness(12, 10, 12, 10),
                    Child = resultContent
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
                .Where(item => item.Result.ExitCode != 0 ||
                               !string.IsNullOrWhiteSpace(item.Result.DiagnosticDetails))
                .Select(item =>
                    $"{item.Package.Name} [{item.Package.Id}]\n" +
                    $"Code: 0x{unchecked((uint)item.Result.ExitCode):X8}\n" +
                    $"{item.Result.Message}" +
                    (string.IsNullOrWhiteSpace(item.Result.DiagnosticDetails)
                        ? string.Empty
                        : $"\n{item.Result.DiagnosticDetails}"))
                .ToList();
            if (technicalDetails.Count > 0)
            {
                string technicalText = string.Join("\n", technicalDetails);
                var technicalPanel = new StackPanel { Spacing = 8 };
                technicalPanel.Children.Add(new TextBlock
                {
                    Text = technicalText,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = (SolidColorBrush)RootGrid.Resources["AppFaintForegroundBrush"]
                });
                var copyButton = new Button
                {
                    Content = en ? "Copy error codes" : "Fehlercodes kopieren",
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                copyButton.Click += (_, __) =>
                {
                    var dataPackage = new DataPackage();
                    dataPackage.SetText(technicalText);
                    Clipboard.SetContent(dataPackage);
                    copyButton.Content = en ? "Copied" : "Kopiert";
                };
                technicalPanel.Children.Add(copyButton);
                panel.Children.Add(new Expander
                {
                    Header = en ? "Technical details" : "Technische Details",
                    IsExpanded = false,
                    Content = technicalPanel
                });
            }

            dialog = CommonUiBuilder.CreateConfirmation(
                RootGrid.XamlRoot,
                en ? "Update summary" : "Update-Abschlussbericht",
                new ScrollViewer { Content = panel, MaxHeight = 430 },
                results.Any(item => item.Result.Status == WingetUpdateStatus.Failed)
                    ? (en ? "Retry failed" : "Fehlgeschlagene erneut versuchen")
                    : null,
                en ? "Close" : "Schließen");
            var previousFocus = FocusManager.GetFocusedElement(RootGrid.XamlRoot) as Control;
            var choice = await dialog.ShowAsync();
            previousFocus?.Focus(FocusState.Programmatic);
            if (choice == ContentDialogResult.Primary)
            {
                var retryResults = await RetryUpdatesAsync(
                    results.Where(item => item.Result.Status == WingetUpdateStatus.Failed),
                    en,
                    CancellationToken.None);
                await ShowUpdateSummaryAsync(retryResults, 0);
            }
        }

        private void SetWingetCardProgress(string packageId, double? percent, bool visible)
        {
            if (!_wingetCardProgressBars.TryGetValue(packageId, out var progressBar)) return;
            progressBar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            progressBar.IsIndeterminate = visible && !percent.HasValue;
            if (percent.HasValue)
                progressBar.Value = Math.Clamp(percent.Value, 0, 100);
        }

        private static string GetUpdateRecommendation(WingetUpdateResult result, bool english)
        {
            if (result.RequiresApplicationShutdown || result.Message.Contains("geschlossen", StringComparison.OrdinalIgnoreCase) ||
                result.Message.Contains("closed", StringComparison.OrdinalIgnoreCase))
                return english ? "Close the affected app and retry the update." : "Schließe das betroffene Programm und versuche das Update erneut.";
            if (result.RequiresElevation)
                return english ? "Retry and approve the Windows administrator prompt." : "Versuche es erneut und bestätige die Windows-Administratorabfrage.";
            if (result.Message.Contains("Internet", StringComparison.OrdinalIgnoreCase) ||
                result.Message.Contains("network", StringComparison.OrdinalIgnoreCase) ||
                result.Message.Contains("Netzwerk", StringComparison.OrdinalIgnoreCase))
                return english ? "Check the internet connection and retry." : "Prüfe die Internetverbindung und versuche es erneut.";
            return english ? "Open technical details, then retry this update individually." : "Öffne die technischen Details und versuche dieses Update anschließend einzeln erneut.";
        }

        private void SetWingetCardProgressColor(string packageId, string brushKey)
        {
            if (_wingetCardProgressBars.TryGetValue(packageId, out var progressBar) &&
                RootGrid.Resources[brushKey] is SolidColorBrush brush)
                progressBar.Foreground = brush;
        }

        private async Task<List<(WingetPackage Package, WingetUpdateResult Result)>> RetryUpdatesAsync(
            IEnumerable<(WingetPackage Package, WingetUpdateResult Result)> failedItems,
            bool english,
            CancellationToken cancellationToken)
        {
            var retryResults = new List<(WingetPackage Package, WingetUpdateResult Result)>();
            foreach (var failedItem in failedItems)
            {
                SetGlobalStatus(english
                    ? $"Retrying {failedItem.Package.Name}..."
                    : $"{failedItem.Package.Name} wird erneut versucht...");
                SetWingetCardStatus(failedItem.Package.Id,
                    english ? "Installing" : "Wird installiert",
                    "AppWarningBrush");
                SetWingetCardProgress(failedItem.Package.Id, null, visible: true);
                SetWingetCardProgressColor(failedItem.Package.Id, "AppAccentBrush");

                bool pendingRestartBefore = RestartDetectionService.IsRestartPending();
                var retry = await UpgradeWithElevationAsync(
                    failedItem.Package.Id,
                    failedItem.Package.Name,
                    new Progress<WingetUpdateProgress>(progress =>
                    {
                        CurrentPackageStatusText.Text = progress.Text;
                        CurrentPackageProgressBar.IsIndeterminate = !progress.Percent.HasValue;
                        if (progress.Percent.HasValue)
                            CurrentPackageProgressBar.Value = progress.Percent.Value;
                        SetWingetCardProgress(
                            failedItem.Package.Id,
                            progress.Percent,
                            visible: true);
                    }),
                    cancellationToken,
                    english);
                bool pendingRestartAfter = RestartDetectionService.IsRestartPending();
                if (!pendingRestartBefore && pendingRestartAfter && retry.Status == WingetUpdateStatus.Successful)
                    retry = retry with
                    {
                        Status = WingetUpdateStatus.RestartRequired,
                        RestartRequired = true,
                        Message = english
                            ? "Installed; Windows reports that a restart is required."
                            : "Installiert; Windows meldet einen erforderlichen Neustart."
                    };
                retryResults.Add((failedItem.Package, retry));
                SetWingetCardProgress(failedItem.Package.Id, 100, visible: true);
                SetWingetCardProgressColor(failedItem.Package.Id, retry.Status switch
                {
                    WingetUpdateStatus.Successful or WingetUpdateStatus.RestartRequired => "AppSuccessBrush",
                    WingetUpdateStatus.Failed => "AppErrorBrush",
                    WingetUpdateStatus.Cancelled => "AppNeutralStatusBrush",
                    _ => "AppWarningBrush"
                });
            }

            retryResults = await VerifyUpdateResultsAsync(retryResults, english, cancellationToken);
            foreach (var retryItem in retryResults)
                LogWingetUpdateActivity(retryItem.Package, retryItem.Result);
            if (_cachedPackages != null)
            {
                RenderWingetPackagesPreservingState(_cachedPackages);
                RestoreVerifiedCardStatuses(retryResults, english);
            }
            CurrentPackageStatusText.Text = "";
            SetGlobalStatus(null);
            return retryResults;
        }

        private void RenderWingetPackagesPreservingState(List<WingetPackage> packages)
        {
            double scrollOffset = MainContentScrollViewer.VerticalOffset;
            var selections = _wingetRows.ToDictionary(
                row => row.Package.Id,
                row => row.Toggle.IsChecked == true,
                StringComparer.OrdinalIgnoreCase);
            RenderWingetPackages(packages);
            foreach (var row in _wingetRows)
                if (selections.TryGetValue(row.Package.Id, out bool selected))
                    row.Toggle.IsChecked = selected;
            UpdateWingetSelectionButton();
            MainContentScrollViewer.ChangeView(null, scrollOffset, null, disableAnimation: true);
        }

        private async Task<WingetUpdateResult> UpgradeWithElevationAsync(
            string packageId,
            string packageName,
            IProgress<WingetUpdateProgress> progress,
            CancellationToken cancellationToken,
            bool english)
        {
            bool knownElevation = WingetElevationPolicy.RequiresElevationBeforeInstall(packageId) ||
                                  _settings.ElevatedUpdateIds.Contains(packageId, StringComparer.OrdinalIgnoreCase);
            bool knownShutdown = WingetElevationPolicy.RequiresApplicationShutdown(packageId) ||
                                 _settings.ShutdownUpdateIds.Contains(packageId, StringComparer.OrdinalIgnoreCase);
            if (knownElevation || knownShutdown)
            {
                bool approved = await ConfirmUpdateRequirementsAsync(
                    packageName, english, knownElevation, knownShutdown);
                if (!approved)
                {
                    return new WingetUpdateResult(
                        WingetUpdateStatus.Cancelled,
                        1223,
                        english
                            ? "Installation as administrator was not started."
                            : "Die Installation als Administrator wurde nicht gestartet.",
                        false);
                }

                if (knownShutdown &&
                    !UpdateApplicationShutdownService.TryCloseForUpdate(packageId, packageName))
                {
                    return new WingetUpdateResult(
                        WingetUpdateStatus.Failed,
                        unchecked((int)0x80073D02),
                        english
                            ? $"{packageName} could not be closed. Close the app manually and try again."
                            : $"{packageName} konnte nicht geschlossen werden. Schließe die App manuell und versuche es erneut.",
                        false);
                }

                return knownElevation
                    ? await RunElevatedUpdateAsync(packageId, packageName, progress, cancellationToken, english, knownShutdown)
                    : await _wingetUpdateService.UpgradeAsync(packageId, progress, cancellationToken);
            }

            var result = await _wingetUpdateService.UpgradeAsync(packageId, progress, cancellationToken);
            if (cancellationToken.IsCancellationRequested ||
                (!result.RequiresElevation && !result.RequiresApplicationShutdown))
                return result;

            if (result.RequiresElevation && !_settings.ElevatedUpdateIds.Contains(packageId, StringComparer.OrdinalIgnoreCase))
                _settings.ElevatedUpdateIds.Add(packageId);
            if (result.RequiresApplicationShutdown && !_settings.ShutdownUpdateIds.Contains(packageId, StringComparer.OrdinalIgnoreCase))
                _settings.ShutdownUpdateIds.Add(packageId);
            _settings.Save();

            bool retryApproved = await ConfirmUpdateRequirementsAsync(
                packageName, english, result.RequiresElevation, result.RequiresApplicationShutdown);
            if (!retryApproved)
            {
                return result with
                {
                    Status = WingetUpdateStatus.Cancelled,
                    Message = english
                        ? "Installation as administrator was not started."
                        : "Die Installation als Administrator wurde nicht gestartet."
                };
            }

            if (result.RequiresApplicationShutdown &&
                !UpdateApplicationShutdownService.TryCloseForUpdate(packageId, packageName))
                return result with
                {
                    Message = english
                        ? $"{packageName} could not be closed. Close the app manually and try again."
                        : $"{packageName} konnte nicht geschlossen werden. Schließe die App manuell und versuche es erneut."
                };

            return result.RequiresElevation
                ? await RunElevatedUpdateAsync(packageId, packageName, progress, cancellationToken, english, result.RequiresApplicationShutdown)
                : await _wingetUpdateService.UpgradeAsync(packageId, progress, cancellationToken);
        }

        private async Task<bool> ConfirmUpdateRequirementsAsync(
            string packageName,
            bool english,
            bool needsElevation,
            bool closesApplication)
        {
            var confirmation = CommonUiBuilder.CreateConfirmation(
                RootGrid.XamlRoot,
                needsElevation
                    ? (english ? "Administrator permission required" : "Administratorrechte erforderlich")
                    : (english ? "App must be closed" : "App muss geschlossen werden"),
                english
                    ? closesApplication
                        ? needsElevation
                            ? $"WinVora will close {packageName} for this update. Administrator permission is also required. Unsaved input in {packageName} may be lost. Windows will ask for confirmation after you continue."
                            : $"WinVora will close {packageName} for this update. Unsaved input may be lost."
                        : $"{packageName} can only be updated with administrator permission. Windows will ask for confirmation after you continue."
                    : closesApplication
                        ? needsElevation
                            ? $"WinVora schließt {packageName} für dieses Update. Zusätzlich sind Administratorrechte erforderlich. Nicht gespeicherte Eingaben in {packageName} können verloren gehen. Anschließend bittet Windows um eine Bestätigung."
                            : $"WinVora schließt {packageName} für dieses Update. Nicht gespeicherte Eingaben können verloren gehen."
                        : $"{packageName} kann nur mit Administratorrechten aktualisiert werden. Nach dem Fortfahren bittet Windows um eine Bestätigung.",
                needsElevation
                    ? (english ? "Install as administrator" : "Als Administrator installieren")
                    : (english ? "Close and install" : "Schließen und installieren"),
                english ? "Cancel" : "Abbrechen");
            var previousFocus = FocusManager.GetFocusedElement(RootGrid.XamlRoot) as Control;
            var result = await confirmation.ShowAsync();
            previousFocus?.Focus(FocusState.Programmatic);
            return result == ContentDialogResult.Primary;
        }

        private async Task<WingetUpdateResult> RunElevatedUpdateAsync(
            string packageId,
            string packageName,
            IProgress<WingetUpdateProgress> progress,
            CancellationToken cancellationToken,
            bool english,
            bool forceApplicationShutdown)
        {
            Logger.Log($"Administratorrechte für Programm-Update erforderlich: {packageName} [{packageId}].");
            SetWingetCardStatus(
                packageId,
                english ? "Administrator approval" : "Administratorbestätigung",
                "AppWarningBrush");
            SetGlobalStatus(english
                ? $"{packageName} needs administrator approval..."
                : $"{packageName} benötigt eine Administratorbestätigung...");

            return await _wingetUpdateService.UpgradeElevatedAsync(
                packageId,
                progress,
                cancellationToken,
                forceApplicationShutdown);
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
