using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;

namespace WinVora
{
    public sealed partial class MainWindow
    {
        private void HistorySearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_currentPageKey == "History") RenderHistoryPage();
        }

        private void HistoryDatePicker_DateChanged(object sender, DatePickerValueChangedEventArgs args)
        {
            if (_currentPageKey == "History") RenderHistoryPage();
        }

        private void HistoryResetFilters_Click(object sender, RoutedEventArgs e)
        {
            _historyFilter = "All";
            HistorySearchBox.Text = "";
            HistoryFromDatePicker.SelectedDate = null;
            HistoryToDatePicker.SelectedDate = null;
            RenderHistoryPage();
        }

        private async void HistoryClearAll_Click(object sender, RoutedEventArgs e)
        {
            if (_settings.ActivityLog.Count == 0) return;
            bool en = Localization.CurrentLanguage == "en";
            if (!await ConfirmAsync(
                en ? "Clear complete history?" : "Gesamten Verlauf leeren?",
                en ? $"All {_settings.ActivityLog.Count} entries will be permanently removed." : $"Alle {_settings.ActivityLog.Count} Einträge werden dauerhaft entfernt.",
                en ? "Clear history" : "Verlauf leeren",
                respectDeleteConfirmationSetting: false)) return;
            _settings.ActivityLog.Clear();
            _settings.Save();
            RenderHistoryPage();
        }
        private void RenderHistoryPage()
        {
            HistoryListPanel.Children.Clear();
            string historyQuery = HistorySearchBox.Text?.Trim() ?? "";
            DateTimeOffset? fromDate = HistoryFromDatePicker.SelectedDate;
            DateTimeOffset? toDate = HistoryToDatePicker.SelectedDate;
            var entries = _settings.ActivityLog.Where(entry =>
                _historyFilter == "All" ||
                (string.IsNullOrWhiteSpace(entry.Result) ? "Successful" : entry.Result) == _historyFilter)
                .Where(entry => string.IsNullOrWhiteSpace(historyQuery) ||
                    entry.TextDe.Contains(historyQuery, StringComparison.OrdinalIgnoreCase) ||
                    entry.TextEn.Contains(historyQuery, StringComparison.OrdinalIgnoreCase) ||
                    (entry.PackageId?.Contains(historyQuery, StringComparison.OrdinalIgnoreCase) ?? false))
                .Where(entry => !fromDate.HasValue || entry.TimestampUtc.ToLocalTime().Date >= fromDate.Value.Date)
                .Where(entry => !toDate.HasValue || entry.TimestampUtc.ToLocalTime().Date <= toDate.Value.Date)
                .ToList();
            foreach (var button in new[] { HistoryAllButton, HistorySuccessButton, HistoryFailedButton, HistoryCancelledButton, HistoryRestartButton })
            {
                bool active = Equals(button.Tag?.ToString(), _historyFilter);
                button.Background = active
                    ? (SolidColorBrush)RootGrid.Resources["AppAccentOverlay20"]
                    : (SolidColorBrush)RootGrid.Resources["AppOverlay10"];
                button.BorderBrush = active
                    ? (SolidColorBrush)RootGrid.Resources["AppAccentBrushLight"]
                    : (SolidColorBrush)RootGrid.Resources["AppOverlay28"];
            }
            PageSubtitle.Text = Localization.CurrentLanguage == "en"
                ? $"{entries.Count} entries"
                : $"{entries.Count} Einträge";

            DateTime? renderedDay = null;
            foreach (var entry in entries)
            {
                var localTime = entry.TimestampUtc.ToLocalTime();
                if (renderedDay != localTime.Date)
                {
                    renderedDay = localTime.Date;
                    string dayTitle = localTime.Date == DateTime.Today
                        ? (Localization.CurrentLanguage == "en" ? "Today" : "Heute")
                        : localTime.Date == DateTime.Today.AddDays(-1)
                            ? (Localization.CurrentLanguage == "en" ? "Yesterday" : "Gestern")
                            : localTime.ToString("D");
                    HistoryListPanel.Children.Add(new TextBlock
                    {
                        Text = dayTitle,
                        FontSize = 18,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Margin = new Thickness(2, 12, 0, 2)
                    });
                }
                string text = Localization.CurrentLanguage == "en" ? entry.TextEn : entry.TextDe;
                string details = string.Join(" · ", new[]
                {
                    entry.PackageId,
                    !string.IsNullOrWhiteSpace(entry.OldVersion) ? $"{entry.OldVersion} → {entry.NewVersion}" : null,
                    entry.ExitCode is int exitCode && exitCode != 0 ? $"0x{unchecked((uint)exitCode):X8}" : null
                }.Where(value => !string.IsNullOrWhiteSpace(value)));
                string? savedReport = Localization.CurrentLanguage == "en" ? entry.DetailsEn : entry.DetailsDe;
                if (!string.IsNullOrWhiteSpace(savedReport))
                    details = savedReport.Split('\n')[0];
                string normalizedResult = string.IsNullOrWhiteSpace(entry.Result) ? "Successful" : entry.Result;
                Windows.UI.Color color = normalizedResult switch
                {
                    "Successful" => Windows.UI.Color.FromArgb(0xFF, 0x4C, 0xD9, 0x73),
                    "RestartRequired" => Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xC1, 0x4D),
                    "Cancelled" => Windows.UI.Color.FromArgb(0xFF, 0xA0, 0xA0, 0xA0),
                    "Failed" => Windows.UI.Color.FromArgb(0xFF, 0xFF, 0x6B, 0x6B),
                    _ => Windows.UI.Color.FromArgb(0xFF, 0x80, 0x80, 0x80)
                };
                var card = MakeInfoCard(text, details, statusBorder: new SolidColorBrush(color));
                if (card.Child is StackPanel historyContent && historyContent.Children.FirstOrDefault() is TextBlock titleBlock)
                {
                    historyContent.Children.RemoveAt(0);
                    var header = new Grid { ColumnSpacing = 12 };
                    header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    string statusText = normalizedResult switch
                    {
                        "Successful" => Localization.CurrentLanguage == "en" ? "Successful" : "Erfolgreich",
                        "RestartRequired" => Localization.CurrentLanguage == "en" ? "Restart required" : "Neustart erforderlich",
                        "Cancelled" => Localization.CurrentLanguage == "en" ? "Cancelled" : "Abgebrochen",
                        "Failed" => Localization.CurrentLanguage == "en" ? "Failed" : "Fehlgeschlagen",
                        _ => Localization.CurrentLanguage == "en" ? "Information" : "Information"
                    };
                    var meta = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
                    meta.Children.Add(new Border
                    {
                        CornerRadius = new CornerRadius(7),
                        Padding = new Thickness(8, 4, 8, 4),
                        Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x24, color.R, color.G, color.B)),
                        Child = new TextBlock { Text = statusText, Foreground = new SolidColorBrush(color), FontSize = 11 }
                    });
                    meta.Children.Add(new TextBlock
                    {
                        Text = entry.TimestampUtc.ToLocalTime().ToString("g"),
                        Foreground = (SolidColorBrush)RootGrid.Resources["AppFaintForegroundBrush"],
                        VerticalAlignment = VerticalAlignment.Center,
                        FontSize = 11
                    });
                    Grid.SetColumn(meta, 1);
                    header.Children.Add(titleBlock);
                    header.Children.Add(meta);
                    historyContent.Children.Insert(0, header);
                }
                card.BorderThickness = new Thickness(4, 0, 0, 0);
                card.MinHeight = 72;
                card.Padding = new Thickness(16, 11, 16, 11);
                if (card.Child is StackPanel detailPanel &&
                    (normalizedResult == "Failed" || entry.ExitCode is not null))
                {
                    var copyDetails = new Button
                    {
                        Content = Localization.CurrentLanguage == "en" ? "Copy technical details" : "Technische Details kopieren",
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Padding = new Thickness(10, 5, 10, 5)
                    };
                    copyDetails.Click += async (_, __) =>
                    {
                        string technical = $"{text}\nPaket: {entry.PackageId}\nVersion: {entry.OldVersion} -> {entry.NewVersion}\nStatus: {normalizedResult}\nExit-Code: {(entry.ExitCode.HasValue ? $"0x{unchecked((uint)entry.ExitCode.Value):X8}" : "-")}\nZeitpunkt: {entry.TimestampUtc.ToLocalTime():O}";
                        var data = new Windows.ApplicationModel.DataTransfer.DataPackage();
                        data.SetText(technical);
                        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
                        ShowInfo(Localization.CurrentLanguage == "en" ? "Technical details copied." : "Technische Details kopiert.", InfoBarSeverity.Success);
                        await Task.CompletedTask;
                    };
                    detailPanel.Children.Add(copyDetails);
                }
                if (card.Child is StackPanel entryPanel)
                {
                    if (!string.IsNullOrWhiteSpace(savedReport))
                    {
                        var showReport = new Button
                        {
                            Content = Localization.CurrentLanguage == "en" ? "Open summary" : "Abschlussbericht öffnen",
                            HorizontalAlignment = HorizontalAlignment.Left,
                            Padding = new Thickness(10, 5, 10, 5)
                        };
                        showReport.Click += async (_, __) =>
                        {
                            var reportDialog = CommonUiBuilder.CreateConfirmation(
                                RootGrid.XamlRoot,
                                Localization.CurrentLanguage == "en" ? "Update session" : "Update-Sitzung",
                                new ScrollViewer
                                {
                                    MaxHeight = 430,
                                    Content = new TextBlock { Text = savedReport, TextWrapping = TextWrapping.Wrap }
                                },
                                null,
                                Localization.CurrentLanguage == "en" ? "Close" : "Schließen");
                            await reportDialog.ShowAsync();
                        };
                        entryPanel.Children.Add(showReport);
                    }
                    var deleteEntry = new Button
                    {
                        Content = Localization.CurrentLanguage == "en" ? "Delete entry" : "Eintrag löschen",
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Padding = new Thickness(10, 5, 10, 5)
                    };
                    deleteEntry.Click += async (_, __) =>
                    {
                        if (!await ConfirmAsync(
                            Localization.CurrentLanguage == "en" ? "Delete history entry?" : "Verlaufseintrag löschen?",
                            text,
                            Localization.CurrentLanguage == "en" ? "Delete" : "Löschen",
                            respectDeleteConfirmationSetting: false)) return;
                        int previousIndex = _settings.ActivityLog.IndexOf(entry);
                        _settings.ActivityLog.Remove(entry);
                        _settings.Save();
                        RenderHistoryPage();
                        ShowUndoInfo(Localization.CurrentLanguage == "en" ? "History entry deleted." : "Verlaufseintrag gelöscht.", () =>
                        {
                            _settings.ActivityLog.Insert(Math.Clamp(previousIndex, 0, _settings.ActivityLog.Count), entry);
                            _settings.Save();
                            RenderHistoryPage();
                        });
                    };
                    entryPanel.Children.Add(deleteEntry);

                    // Sekundäre Aktionen bleiben eingeklappt, damit viele
                    // Verlaufseinträge als kompakte Übersicht sichtbar sind.
                    int fixedChildren = string.IsNullOrWhiteSpace(details) ? 1 : 2;
                    if (entryPanel.Children.Count > fixedChildren)
                    {
                        var detailActions = new StackPanel
                        {
                            Spacing = 8,
                            Visibility = Visibility.Collapsed
                        };
                        while (entryPanel.Children.Count > fixedChildren)
                        {
                            var child = entryPanel.Children[fixedChildren];
                            entryPanel.Children.RemoveAt(fixedChildren);
                            detailActions.Children.Add(child);
                        }
                        string expansionKey = $"{entry.TimestampUtc.Ticks}:{entry.TextDe}:{entry.PackageId}";
                        bool initiallyExpanded = _expandedHistoryEntries.Contains(expansionKey);
                        detailActions.Visibility = initiallyExpanded ? Visibility.Visible : Visibility.Collapsed;
                        var toggleDetails = new Button
                        {
                            Content = initiallyExpanded
                                ? (Localization.CurrentLanguage == "en" ? "Hide details" : "Details ausblenden")
                                : (Localization.CurrentLanguage == "en" ? "Show details" : "Details anzeigen"),
                            HorizontalAlignment = HorizontalAlignment.Left,
                            Padding = new Thickness(10, 5, 10, 5)
                        };
                        toggleDetails.Click += (_, __) =>
                        {
                            bool show = detailActions.Visibility != Visibility.Visible;
                            detailActions.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                            if (show) _expandedHistoryEntries.Add(expansionKey);
                            else _expandedHistoryEntries.Remove(expansionKey);
                            toggleDetails.Content = show
                                ? (Localization.CurrentLanguage == "en" ? "Hide details" : "Details ausblenden")
                                : (Localization.CurrentLanguage == "en" ? "Show details" : "Details anzeigen");
                        };
                        entryPanel.Children.Add(toggleDetails);
                        entryPanel.Children.Add(detailActions);
                    }
                }
                HistoryListPanel.Children.Add(card);
            }

            if (entries.Count == 0)
                HistoryListPanel.Children.Add(MakeEmptyState(
                    "\uE81C",
                    Localization.CurrentLanguage == "en" ? "No matching entries" : "Keine passenden Einträge",
                    Localization.CurrentLanguage == "en" ? "Choose another filter to see more entries." : "Wähle einen anderen Filter, um weitere Einträge zu sehen."));
        }

        private async void ExportHistory_Click(object sender, RoutedEventArgs e)
        {
            var lines = _settings.ActivityLog.Select(entry =>
                $"{entry.TimestampUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss} | {entry.TextDe} | {entry.PackageId} | " +
                $"{entry.OldVersion} -> {entry.NewVersion} | {entry.Result} | Exit={entry.ExitCode}");
            if (await ReportExportService.SaveTextAsync(this, $"WinVora-Verlauf-{DateTime.Now:yyyyMMdd}", string.Join(Environment.NewLine, lines)))
                ShowInfo(Localization.CurrentLanguage == "en" ? "History exported." : "Verlauf wurde exportiert.", InfoBarSeverity.Success);
        }

        private async void ExportSystemReport_Click(object sender, RoutedEventArgs e)
        {
            _cachedSnapshot ??= await SystemInfoProvider.GetFullSnapshotAsync(_startupCancellation.Token);
            var s = _cachedSnapshot;
            string report = string.Join(Environment.NewLine + Environment.NewLine, new[]
            {
                "WINVORA SYSTEMBERICHT",
                $"Erstellt: {DateTime.Now:g}",
                SystemInfoFormatter.Device(s), SystemInfoFormatter.OperatingSystem(s),
                SystemInfoFormatter.Cpu(s, Localization.CurrentLanguage == "en"),
                SystemInfoFormatter.Ram(s, Localization.CurrentLanguage == "en"),
                SystemInfoFormatter.Board(s), SystemInfoFormatter.Security(s),
                "Grafik:" + Environment.NewLine + SystemInfoFormatter.Gpus(s),
                "Laufwerke:" + Environment.NewLine + SystemInfoFormatter.Drives(s),
                "Netzwerk:" + Environment.NewLine + SystemInfoFormatter.Network(s),
                "Akku: " + SystemInfoFormatter.Battery(s)
            });
            if (await ReportExportService.SaveTextAsync(this, $"WinVora-Systembericht-{DateTime.Now:yyyyMMdd}", report))
                ShowInfo(Localization.CurrentLanguage == "en" ? "System report exported." : "Systembericht wurde exportiert.", InfoBarSeverity.Success);
        }
    }
}
