using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ToolkitControls = CommunityToolkit.WinUI.Controls;

namespace WinVora
{
    public sealed partial class MainWindow
    {
        // Baut die Update-Karten auf und stößt das Nachladen der Details an.
        // Ausgelagert, damit sowohl ein frischer winget-Aufruf als auch ein
        // gecachtes Ergebnis (siehe Bug #6-Fix) darüber angezeigt werden können.
        private void RenderWingetPackages(List<WingetPackage> packages)
        {
            ContentArea.Children.Clear();
            _wingetRows.Clear();
            _wingetIconCards.Clear();
            _loadedWingetIcons.Clear();
            _wingetStatusBadges.Clear();
            _wingetCardProgressBars.Clear();

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
                    var hiddenActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                    hiddenActions.Children.Add(CommonUiBuilder.CreateStatusBadge(
                        deferred.HiddenUntilUtc.HasValue ? (en ? "Postponed" : "Zurückgestellt") : (en ? "Ignored" : "Ignoriert"),
                        warning: true, resources: RootGrid.Resources));
                    hiddenActions.Children.Add(restore);
                    ContentArea.Children.Add(new ToolkitControls.SettingsCard
                    {
                        Header = package?.Name ?? deferred.PackageId,
                        Description = until,
                        HeaderIcon = new FontIcon { Glyph = "\uE823" },
                        Content = hiddenActions,
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
                    var ignoredActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                    ignoredActions.Children.Add(CommonUiBuilder.CreateStatusBadge(
                        en ? "Ignored" : "Ignoriert", warning: true, resources: RootGrid.Resources));
                    ignoredActions.Children.Add(restore);
                    ContentArea.Children.Add(new ToolkitControls.SettingsCard
                    {
                        Header = package?.Name ?? ignoredId,
                        Description = en ? "Permanently ignored" : "Dauerhaft ignoriert",
                        HeaderIcon = new FontIcon { Glyph = "\uE823" },
                        Content = ignoredActions,
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
            _viewState.RetainUpdates(packages.Select(package => package.Id).ToHashSet(StringComparer.OrdinalIgnoreCase));

            foreach (var pkg in packages)
            {
                var toggle = new CheckBox
                {
                    IsChecked = _viewState.IsUpdateSelected(pkg.Id),
                    VerticalAlignment = VerticalAlignment.Center,
                    MinWidth = 22,
                    Padding = new Thickness(0)
                };
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
                var statusText = new TextBlock
                {
                    Text = en ? "Ready" : "Bereit",
                    FontSize = 11,
                    Foreground = (SolidColorBrush)RootGrid.Resources["AppAccentBrushLight"],
                    VerticalAlignment = VerticalAlignment.Center
                };
                var statusBadge = new Border
                {
                    Background = (SolidColorBrush)RootGrid.Resources["AppAccentOverlay10"],
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(8, 4, 8, 4),
                    Child = statusText
                };
                _wingetStatusBadges[pkg.Id] = statusText;

                var cardProgress = new ProgressBar
                {
                    Minimum = 0,
                    Maximum = 100,
                    Height = 4,
                    Visibility = Visibility.Collapsed,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                _wingetCardProgressBars[pkg.Id] = cardProgress;

                var cardActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                cardActions.Children.Add(statusBadge);
                cardActions.Children.Add(detailsButton);
                cardActions.Children.Add(deferButton);

                var cardContent = new StackPanel
                {
                    Spacing = 8,
                    MinWidth = 230
                };
                cardContent.Children.Add(cardActions);
                cardContent.Children.Add(cardProgress);

                var compactHeader = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 10,
                    VerticalAlignment = VerticalAlignment.Center
                };
                compactHeader.Children.Add(toggle);
                var packageNameText = new TextBlock
                {
                    Text = pkg.Name,
                    FontSize = 15,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                    MaxWidth = 300,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap
                };
                ToolTipService.SetToolTip(packageNameText, pkg.Name);
                compactHeader.Children.Add(packageNameText);

                bool requiresElevation = WingetElevationPolicy.RequiresElevationBeforeInstall(pkg.Id) ||
                                         _settings.ElevatedUpdateIds.Contains(pkg.Id, StringComparer.OrdinalIgnoreCase);
                bool requiresShutdown = WingetElevationPolicy.RequiresApplicationShutdown(pkg.Id) ||
                                        _settings.ShutdownUpdateIds.Contains(pkg.Id, StringComparer.OrdinalIgnoreCase);
                string requirementHint = requiresElevation && requiresShutdown
                    ? (en ? " · Closes app · Administrator approval" : " · Schließt App · Administratorbestätigung")
                    : requiresElevation
                        ? (en ? " · Administrator approval" : " · Administratorbestätigung")
                        : requiresShutdown
                            ? (en ? " · Closes app" : " · Schließt App")
                            : string.Empty;

                var card = new ToolkitControls.SettingsCard
                {
                    Header = compactHeader,
                    Description = $"{baseDescription}     {sizeLabel.ToUpperInvariant()}  {loadingLabel}     {publisherLabel.ToUpperInvariant()}  {loadingLabel}{requirementHint}",
                    HeaderIcon = new FontIcon { Glyph = "\uE7B8" }, // Platzhalter-App-Icon
                    Content = cardContent,
                    Background = (SolidColorBrush)RootGrid.Resources["AppCardSurfaceBrush"],
                    BorderThickness = new Thickness(0),
                    BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                    Tag = detailsText
                };
                card.MinHeight = 88;
                card.Padding = new Thickness(14, 10, 14, 10);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(card,
                    en ? $"Available update for {pkg.Name}" : $"Verfügbares Update für {pkg.Name}");

                void UpdateSelectionVisual()
                {
                    _viewState.SetUpdateSelected(pkg.Id, toggle.IsChecked == true);
                    if (_isBulkUpdatingWingetSelection) return;
                    UpdateWingetSelectionButton();
                }
                toggle.Checked += (_, __) => UpdateSelectionVisual();
                toggle.Unchecked += (_, __) => UpdateSelectionVisual();

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
            var previousDeferred = _settings.DeferredUpdates
                .Where(entry => entry.PackageId.Equals(package.Id, StringComparison.OrdinalIgnoreCase))
                .Select(entry => new DeferredUpdateEntry { PackageId = entry.PackageId, HiddenUntilUtc = entry.HiddenUntilUtc })
                .ToList();
            bool previouslyIgnored = _settings.IgnoredUpdateIds.Contains(package.Id, StringComparer.OrdinalIgnoreCase);
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
            ShowUndoInfo(days.HasValue
                ? $"{package.Name} wurde für {days.Value} Tag(e) zurückgestellt."
                : $"{package.Name} wird dauerhaft ignoriert.", () =>
                {
                    _settings.DeferredUpdates.RemoveAll(entry => entry.PackageId.Equals(package.Id, StringComparison.OrdinalIgnoreCase));
                    _settings.IgnoredUpdateIds.RemoveAll(id => id.Equals(package.Id, StringComparison.OrdinalIgnoreCase));
                    _settings.DeferredUpdates.AddRange(previousDeferred);
                    if (previouslyIgnored) _settings.IgnoredUpdateIds.Add(package.Id);
                    _settings.Save();
                    if (_cachedPackages != null) RenderWingetPackages(_cachedPackages);
                });
        }
    }
}
