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

            // BUGFIX (Lag-Problem): Die komplette Prozess-Kommunikation (Start,
            // Zeile-für-Zeile-Lesen, Parsen) läuft jetzt in Task.Run auf einem
            // Hintergrund-Thread. Vorher lief "await p.StandardOutput.ReadLineAsync()"
            // direkt in dieser Methode, deren Fortsetzung nach jedem await automatisch
            // wieder auf den UI-Thread (SynchronizationContext) zurückspringt. Bei
            // vielen Paketzeilen bedeutet das sehr viele kurze Rücksprünge zum
            // UI-Thread hintereinander, was die Oberfläche spürbar ruckeln lässt,
            // während winget noch Daten liefert. Läuft alles in Task.Run, bleibt
            // der UI-Thread währenddessen frei.
            try
            {
                packages = await Task.Run(() =>
                {
                    var result = new List<WingetPackage>();
                    string? headerLine = null;
                    int[]? columns = null;

                    using var p = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "winget",
                            Arguments = "upgrade",
                            RedirectStandardOutput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            StandardOutputEncoding = Encoding.UTF8
                        }
                    };

                    p.Start();
                    using var cancellationRegistration = loadCancellationToken.Register(() =>
                    {
                        try
                        {
                            if (!p.HasExited) p.Kill(entireProcessTree: true);
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError("Abbruch der WinGet-Startprüfung", ex);
                        }
                    });

                    bool hasStartedRows = false;
                    string? line;

                    while ((line = p.StandardOutput.ReadLine()) != null)
                    {
                        loadCancellationToken.ThrowIfCancellationRequested();
                        if (string.IsNullOrWhiteSpace(line))
                        {
                            // Nach den ersten echten Paketzeilen markiert eine Leerzeile
                            // das Ende der Tabelle - alles danach ist nur noch die
                            // Zusammenfassungszeile ("X Aktualisierungen verfügbar.") o.ä.
                            if (hasStartedRows) break;
                            continue;
                        }

                        // Trennzeile ("--------------") markiert das Ende der Kopfzeile.
                        // Funktioniert sprachunabhängig (Deutsch/Englisch/...).
                        if (line.TrimStart().StartsWith("-") && headerLine != null && columns == null)
                        {
                            columns = GetColumnStarts(headerLine);
                            continue;
                        }

                        if (columns == null)
                        {
                            headerLine = line;
                            continue;
                        }

                        // Echte Paketzeilen haben immer mehrere Leerzeichen zwischen den
                        // Spalten. Die Zusammenfassungszeile am Ende ("X Aktualisierungen
                        // verfügbar." / "X upgrades available.") ist normaler Fließtext
                        // ohne solche Lücken - dort brechen wir das Einlesen ab.
                        if (!line.Contains("  "))
                        {
                            if (hasStartedRows) break;
                            continue;
                        }

                        var pkg = Parse(line, columns);
                        if (pkg != null && !string.IsNullOrWhiteSpace(pkg.Id))
                        {
                            result.Add(pkg);
                            hasStartedRows = true;
                        }
                    }

                    p.WaitForExit();

                    // Für spätere Aufrufe (z.B. LoadWingetDetailsInBackground) merken.
                    _wingetColumns = columns;

                    return result;
                });
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

        // Baut die Update-Karten auf und stößt das Nachladen der Details an.
        // Ausgelagert, damit sowohl ein frischer winget-Aufruf als auch ein
        // gecachtes Ergebnis (siehe Bug #6-Fix) darüber angezeigt werden können.
        private void RenderWingetPackages(List<WingetPackage> packages)
        {
            ContentArea.Children.Clear();
            _wingetRows.Clear();

            bool en = Localization.CurrentLanguage == "en";
            DateTime now = DateTime.UtcNow;
            _settings.DeferredUpdates.RemoveAll(entry => entry.HiddenUntilUtc.HasValue && entry.HiddenUntilUtc <= now);
            var allPackages = packages.ToList();
            var hiddenIds = _settings.DeferredUpdates.Select(entry => entry.PackageId)
                .Concat(_settings.IgnoredUpdateIds)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            int hiddenCount = packages.Count(package => hiddenIds.Contains(package.Id));
            packages = packages.Where(package => !hiddenIds.Contains(package.Id)).ToList();
            _settings.Save();
            var publisherLabel = Localization.T("Winget.Publisher");
            var sizeLabel = Localization.T("Winget.Size");
            var loadingLabel = Localization.T("Winget.Loading");

            if (hiddenCount > 0)
            {
                ContentArea.Children.Add(new TextBlock
                {
                    Text = en ? "Postponed and ignored updates" : "Zurückgestellte und ignorierte Updates",
                    FontSize = 17,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 4)
                });
                foreach (var deferred in _settings.DeferredUpdates.ToList())
                {
                    var package = allPackages.FirstOrDefault(item => item.Id.Equals(deferred.PackageId, StringComparison.OrdinalIgnoreCase));
                    string until = deferred.HiddenUntilUtc.HasValue
                        ? deferred.HiddenUntilUtc.Value.ToLocalTime().ToString("g")
                        : (en ? "Permanently ignored" : "Dauerhaft ignoriert");
                    var restore = new Button { Content = en ? "Restore" : "Wieder anzeigen" };
                    restore.Click += (_, __) =>
                    {
                        _settings.DeferredUpdates.Remove(deferred);
                        _settings.IgnoredUpdateIds.RemoveAll(id =>
                            id.Equals(deferred.PackageId, StringComparison.OrdinalIgnoreCase));
                        _settings.Save();
                        if (_cachedPackages != null) RenderWingetPackages(_cachedPackages);
                    };
                    ContentArea.Children.Add(new ToolkitControls.SettingsCard
                    {
                        Header = package?.Name ?? deferred.PackageId,
                        Description = until,
                        HeaderIcon = new FontIcon { Glyph = "\uE823" },
                        Content = restore,
                        CornerRadius = new CornerRadius(12)
                    });
                }
                foreach (var ignoredId in _settings.IgnoredUpdateIds.ToList())
                {
                    if (_settings.DeferredUpdates.Any(entry =>
                        entry.PackageId.Equals(ignoredId, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    var package = allPackages.FirstOrDefault(item =>
                        item.Id.Equals(ignoredId, StringComparison.OrdinalIgnoreCase));
                    var restore = new Button { Content = en ? "Restore" : "Wieder anzeigen" };
                    restore.Click += (_, __) =>
                    {
                        _settings.IgnoredUpdateIds.RemoveAll(id =>
                            id.Equals(ignoredId, StringComparison.OrdinalIgnoreCase));
                        _settings.Save();
                        if (_cachedPackages != null) RenderWingetPackages(_cachedPackages);
                    };
                    ContentArea.Children.Add(new ToolkitControls.SettingsCard
                    {
                        Header = package?.Name ?? ignoredId,
                        Description = en ? "Permanently ignored" : "Dauerhaft ignoriert",
                        HeaderIcon = new FontIcon { Glyph = "\uE823" },
                        Content = restore,
                        CornerRadius = new CornerRadius(12)
                    });
                }
            }

            if (packages.Count == 0)
            {
                PageSubtitle.Text = en ? "No updates available" : "Keine Updates verfügbar";
                HealthUpdatesText.Text = Localization.T("Common.None");
                _wingetSelectAllState = false;
                UpdateWingetSelectAllAppearance();

                ContentArea.Children.Add(MakeEmptyState(
                    "\uE895",
                    en ? "Everything is up to date" : "Alles ist aktuell",
                    en ? "No program updates were found." : "Es wurden keine Programm-Updates gefunden.",
                    en ? "Check again" : "Erneut prüfen",
                    async () => await LoadWinget(forceRefresh: true)));
                return;
            }

            PageSubtitle.Text = (packages.Count == 1
                ? (en ? "1 app has an update" : "1 App hat ein Update")
                : (en ? $"{packages.Count} apps have updates" : $"{packages.Count} Apps haben Updates")) +
                (hiddenCount > 0 ? (en ? $" · {hiddenCount} hidden" : $" · {hiddenCount} ausgeblendet") : "");

            HealthUpdatesText.Text = packages.Count.ToString();
            UpdateDashboardStatusSummary();

            // Pakete starten standardmäßig alle ausgewählt (IsOn = true weiter
            // unten) - der Button muss also mit "Alle abwählen" starten.
            _wingetSelectAllState = true;
            UpdateWingetSelectAllAppearance();

            foreach (var pkg in packages)
            {
                var toggle = new ToggleSwitch { IsOn = true, OnContent = "", OffContent = "" };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle,
                    en ? $"Select update for {pkg.Name}" : $"Update für {pkg.Name} auswählen");
                var baseDescription = UpdateUiBuilder.VersionSummary(pkg, en);

                var deferButton = new Button
                {
                    Content = "⋯",
                    Width = 40,
                    Height = 34,
                    Padding = new Thickness(0)
                };
                ToolTipService.SetToolTip(deferButton,
                    en ? "Postpone or ignore update" : "Update zurückstellen oder ignorieren");
                var deferMenu = new MenuFlyout();
                foreach (var option in new (string Label, int? Days)[]
                {
                    (en ? "Hide for 1 day" : "1 Tag zurückstellen", 1),
                    (en ? "Hide for 7 days" : "7 Tage zurückstellen", 7),
                    (en ? "Hide for 30 days" : "30 Tage zurückstellen", 30),
                    (en ? "Ignore permanently" : "Dauerhaft ignorieren", null)
                })
                {
                    var menuItem = new MenuFlyoutItem { Text = option.Label };
                    menuItem.Click += (_, __) => DeferUpdate(pkg, option.Days);
                    deferMenu.Items.Add(menuItem);
                }
                deferButton.Flyout = deferMenu;
                var detailsButton = new Button
                {
                    Content = en ? "Details" : "Details",
                    Height = 34,
                    Padding = new Thickness(10, 4, 10, 4)
                };
                ToolTipService.SetToolTip(detailsButton, en ? "Show technical package details" : "Technische Paketdetails anzeigen");
                var detailsText = new TextBlock
                {
                    Text = UpdateUiBuilder.TechnicalDetails(pkg, en),
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 420
                };
                detailsButton.Flyout = new Flyout
                {
                    Content = detailsText
                };
                var cardActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
                cardActions.Children.Add(new Border
                {
                    Background = (SolidColorBrush)RootGrid.Resources["AppAccentOverlay20"],
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(9, 5, 9, 5),
                    Child = new TextBlock
                    {
                        Text = en ? "Available" : "Verfügbar",
                        FontSize = 12,
                        Foreground = (SolidColorBrush)RootGrid.Resources["AppAccentBrushLight"]
                    }
                });
                cardActions.Children.Add(toggle);
                cardActions.Children.Add(detailsButton);
                cardActions.Children.Add(deferButton);

                var card = new ToolkitControls.SettingsCard
                {
                    Header = pkg.Name,
                    Description = $"{baseDescription}\n{sizeLabel.ToUpperInvariant()}   {loadingLabel}     {publisherLabel.ToUpperInvariant()}   {loadingLabel}",
                    HeaderIcon = new FontIcon { Glyph = "\uE7B8" }, // Platzhalter-App-Icon
                    Content = cardActions,
                    BorderThickness = new Thickness(1),
                    BorderBrush = (SolidColorBrush)RootGrid.Resources["AppAccentBrushLight"], // startet ausgewählt
                    Tag = detailsText
                };
                card.MinHeight = 128;
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(card,
                    en ? $"Available update for {pkg.Name}" : $"Verfügbares Update für {pkg.Name}");

                // Akzentfarbener Rand, solange das Paket zum Aktualisieren ausgewählt ist.
                var defaultBorder = (SolidColorBrush)RootGrid.Resources["AppOverlay28"];
                var accentBorder = (SolidColorBrush)RootGrid.Resources["AppAccentBrushLight"];
                toggle.Toggled += (_, __) => card.BorderBrush = toggle.IsOn ? accentBorder : defaultBorder;
                toggle.Toggled += (_, __) => UpdateWingetSelectionButton();

                ContentArea.Children.Add(card);
                _wingetRows.Add((pkg, toggle, card, baseDescription));
            }

            _wingetNoResultsText = new TextBlock
            {
                Text = en ? "No updates match your search." : "Keine Updates passen zu deiner Suche.",
                FontSize = 14,
                Foreground = (SolidColorBrush)RootGrid.Resources["AppFaintForegroundBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 20, 0, 0),
                Visibility = Visibility.Collapsed
            };
            ContentArea.Children.Add(_wingetNoResultsText);
            UpdateWingetSelectionButton();

            // Herausgeber und Größe laufen im Hintergrund nach (winget show pro Paket),
            // damit die Liste sofort erscheint und nicht auf alle Detailabfragen wartet.
            _ = LoadWingetDetailsInBackground(_wingetRows.ToList());

            // Echte App-Icons nachladen: winget-Pakete sind ja bereits installierte
            // Programme (es werden nur Updates aufgelistet) - wir suchen sie anhand
            // des Namens in der Registry und extrahieren ihr echtes Icon.
            _ = LoadWingetIconsInBackground(_wingetRows.ToList());
        }

        private void DeferUpdate(WingetPackage package, int? days)
        {
            _settings.DeferredUpdates.RemoveAll(entry =>
                entry.PackageId.Equals(package.Id, StringComparison.OrdinalIgnoreCase));
            _settings.IgnoredUpdateIds.RemoveAll(id =>
                id.Equals(package.Id, StringComparison.OrdinalIgnoreCase));
            if (days.HasValue)
            {
                _settings.DeferredUpdates.Add(new DeferredUpdateEntry
                {
                    PackageId = package.Id,
                    HiddenUntilUtc = DateTime.UtcNow.AddDays(days.Value)
                });
            }
            else
            {
                _settings.IgnoredUpdateIds.Add(package.Id);
            }
            _settings.Save();
            if (_cachedPackages != null) RenderWingetPackages(_cachedPackages);
            ShowInfo(days.HasValue
                ? $"{package.Name} wurde für {days.Value} Tag(e) zurückgestellt."
                : $"{package.Name} wird dauerhaft ignoriert.");
        }
    }
}
