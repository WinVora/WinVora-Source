using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ToolkitControls = CommunityToolkit.WinUI.Controls;

namespace WinVora
{
    public sealed partial class MainWindow
    {
        // ================= APPS / WINGET =================

        private async void Updates_Click(object sender, RoutedEventArgs e)
        {
            SetPage("Updates");
            // Auf der Winget-Seite selbst soll immer der echte, aktuelle Stand
            // geholt werden - hier macht Caching keinen Sinn.
            await LoadWinget(forceRefresh: true);
        }

        private void WingetSelectAll_Click(object sender, RoutedEventArgs e)
        {
            var visibleRows = _wingetRows.Where(r => r.Card.Visibility == Visibility.Visible).ToList();
            if (visibleRows.Count == 0) return;

            _wingetSelectAllState = !_wingetSelectAllState;
            bool newState = _wingetSelectAllState;

            foreach (var row in visibleRows)
                row.Toggle.IsOn = newState;

            UpdateWingetSelectAllAppearance();
            UpdateWingetSelectionButton();
        }

        private void UpdateWingetSelectAllAppearance()
        {
            bool allSelected = _wingetSelectAllState;
            WingetSelectAllText.Text = Localization.T(allSelected ? "Common.DeselectAll" : "Common.SelectAll");
            WingetSelectAllIcon.Visibility = allSelected ? Visibility.Visible : Visibility.Collapsed;
            WingetSelectAllIndicator.Background = allSelected
                ? (SolidColorBrush)RootGrid.Resources["AppAccentBrush"]
                : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }

        private void UpdateWingetSelectionButton()
        {
            int count = _wingetRows.Count(row => row.Toggle.IsOn);
            var visibleSelection = SelectionSummary.From(_wingetRows
                .Where(row => row.Card.Visibility == Visibility.Visible)
                .Select(row => row.Toggle.IsOn));
            int visibleCount = visibleSelection.Total;
            int visibleSelected = visibleSelection.Selected;
            _wingetSelectAllState = visibleSelection.All;
            UpdateWingetSelectAllAppearance();
            WingetSelectAllIndicator.Opacity = visibleSelection.Partial ? 0.55 : 1;
            WingetSelectAllIcon.Glyph = visibleSelection.Partial ? "\uE738" : "\uE73E";
            WingetSelectAllIcon.Visibility = visibleSelected > 0 ? Visibility.Visible : Visibility.Collapsed;
            bool en = Localization.CurrentLanguage == "en";
            StartUpdateButton.Content = count == 1
                ? (en ? "Install 1 update" : "1 Update installieren")
                : (en ? $"Install {count} updates" : $"{count} Updates installieren");
            StartUpdateButton.IsEnabled = count > 0 && !_isLoadingWinget && !_isUpdatingWinget;
        }

        private async void WingetSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _wingetSearchDebounce?.Cancel();
            var debounce = _wingetSearchDebounce = new CancellationTokenSource();
            try { await Task.Delay(160, debounce.Token); }
            catch (OperationCanceledException) { return; }
            if (debounce != _wingetSearchDebounce) return;

            var query = WingetSearchBox.Text?.Trim() ?? "";
            WingetClearSearchButton.Visibility = string.IsNullOrEmpty(query) ? Visibility.Collapsed : Visibility.Visible;

            foreach (var row in _wingetRows)
            {
                row.Card.Visibility = string.IsNullOrEmpty(query) ||
                    row.Package.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    row.Package.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
                        ? Visibility.Visible
                        : Visibility.Collapsed;
            }

            var visibleCount = _wingetRows.Count(r => r.Card.Visibility == Visibility.Visible);
            if (_wingetNoResultsText != null)
                _wingetNoResultsText.Visibility = visibleCount == 0 && !string.IsNullOrEmpty(query)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            bool en = Localization.CurrentLanguage == "en";
            PageSubtitle.Text = string.IsNullOrEmpty(query)
                ? (_wingetRows.Count == 1
                    ? (en ? "1 app has an update" : "1 App hat ein Update")
                    : (en ? $"{_wingetRows.Count} apps have updates" : $"{_wingetRows.Count} Apps haben Updates"))
                : (en
                    ? $"Showing {visibleCount} of {_wingetRows.Count} updates"
                    : $"{visibleCount} von {_wingetRows.Count} Updates angezeigt");
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadWinget(forceRefresh: true);
        }

        private async Task LoadWinget(bool forceRefresh = false)
        {
            if (_isLoadingWinget || _isUpdatingWinget) return;
            var loadCancellationToken = _startupCancellation.Token;
            // BUGFIX (Teil 2): Wenn schon ein Ergebnis vorliegt und kein
            // erzwungener Refresh angefordert wurde, einfach das gecachte
            // Ergebnis erneut anzeigen statt "winget upgrade" neu zu starten.
            if (!forceRefresh && _cachedPackages != null)
            {
                RenderWingetPackages(_cachedPackages);
                return;
            }

            _isLoadingWinget = true;
            SetGlobalStatus(Localization.CurrentLanguage == "en" ? "Checking program updates..." : "Programm-Updates werden geprüft...");
            ContentArea.Children.Clear();
            ContentArea.Children.Add(new TextBlock
            {
                Text = Localization.CurrentLanguage == "en" ? "Checking available updates..." : "Verfügbare Updates werden geprüft...",
                Foreground = (SolidColorBrush)RootGrid.Resources["AppMutedForegroundBrush"],
                Margin = new Thickness(4, 16, 0, 8)
            });
            _wingetRows.Clear();
            WingetSearchBox.Text = "";
            _wingetColumns = null; // bei jedem Aufruf zurücksetzen

            RefreshButton.IsEnabled = false;
            StartUpdateButton.IsEnabled = false;

            UpdatesLoadingRing.Visibility = Visibility.Visible;
            UpdatesLoadingRing.IsActive = true;

            List<WingetPackage> packages = new();
            bool hadError = false;
            bool wingetNotFound = false;
            string? errorMessage = null;

            try
            {
                var discovery = await WingetDiscoveryService.GetUpgradesAsync(loadCancellationToken);
                packages = discovery.Packages;
                _wingetColumns = discovery.Columns;
            }
            catch (System.ComponentModel.Win32Exception winEx) when (winEx.NativeErrorCode == 2)
            {
                // NativeErrorCode 2 = ERROR_FILE_NOT_FOUND -> winget.exe wurde nicht gefunden
                hadError = true;
                wingetNotFound = true;
                Logger.Log("winget wurde nicht gefunden (ERROR_FILE_NOT_FOUND).");
            }
            catch (Exception ex)
            {
                hadError = true;
                errorMessage = ex.Message;
                Logger.LogError("LoadWinget", ex);
            }
            finally
            {
                _isLoadingWinget = false;
                UpdatesLoadingRing.IsActive = false;
                UpdatesLoadingRing.Visibility = Visibility.Collapsed;
                RefreshButton.IsEnabled = true;
                StartUpdateButton.IsEnabled = true;
                SetGlobalStatus(null);
            }

            if (hadError)
            {
                bool en = Localization.CurrentLanguage == "en";

                if (wingetNotFound)
                {
                    PageSubtitle.Text = en ? "winget was not found" : "winget wurde nicht gefunden";
                    HealthUpdatesText.Text = "N/A";

                    ContentArea.Children.Add(new TextBlock
                    {
                        Text = en
                            ? "winget is not installed or not available in PATH. " +
                              "Install the \"App Installer\" (Windows Package Manager) from the Microsoft Store " +
                              "and restart WinVora afterwards."
                            : "winget ist nicht installiert oder nicht im PATH verfügbar. " +
                              "Installiere den \"App Installer\" (Windows-Paketmanager) über den Microsoft Store " +
                              "und starte WinVora danach neu.",
                        Foreground = new SolidColorBrush(Microsoft.UI.Colors.OrangeRed),
                        TextWrapping = TextWrapping.Wrap
                    });
                }
                else
                {
                    var technicalDetails = new Expander
                    {
                        Header = en ? "Technical details" : "Technische Details",
                        IsExpanded = false,
                        Content = new StackPanel
                        {
                            Spacing = 8,
                            Children =
                            {
                                new TextBlock
                                {
                                    Text = errorMessage ?? (en ? "No additional details." : "Keine weiteren Details."),
                                    TextWrapping = TextWrapping.Wrap,
                                    Foreground = (SolidColorBrush)RootGrid.Resources["AppFaintForegroundBrush"]
                                },
                                new Button
                                {
                                    Content = en ? "Copy details" : "Details kopieren",
                                    HorizontalAlignment = HorizontalAlignment.Left
                                }
                            }
                        }
                    };
                    if (technicalDetails.Content is StackPanel technicalPanel && technicalPanel.Children[1] is Button copyDetailsButton)
                    {
                        copyDetailsButton.Click += (_, __) =>
                        {
                            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
                            package.SetText(errorMessage ?? "");
                            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
                            ShowInfo(en ? "Technical details copied." : "Technische Details kopiert.", InfoBarSeverity.Success);
                        };
                    }
                    ContentArea.Children.Add(MakeEmptyState(
                        "\uE783",
                        en ? "Updates could not be checked" : "Updates konnten nicht geprüft werden",
                        en ? "Check your internet connection and try again." : "Prüfe deine Internetverbindung und versuche es erneut.",
                        en ? "Try again" : "Erneut versuchen",
                        async () => await LoadWinget(forceRefresh: true)));
                    ContentArea.Children.Add(technicalDetails);
                }

                // Fehlermeldung wurde bereits oben in ContentArea angezeigt.
                // Kein Caching eines Fehlerzustands, damit beim nächsten
                // Aufruf automatisch erneut versucht wird.
                return;
            }

            _cachedPackages = packages;
            RenderWingetPackages(packages);
        }

    }
}
