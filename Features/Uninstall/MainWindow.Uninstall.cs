using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ToolkitControls = CommunityToolkit.WinUI.Controls;

namespace WinVora
{
    public sealed partial class MainWindow
    {
        // ================= DEINSTALLIEREN =================

        private List<InstalledProgram> _installedPrograms = new();

        private async void Uninstaller_Click(object sender, RoutedEventArgs e)
        {
            SetPage("Uninstall");
            await LoadInstalledPrograms();
        }

        private async void UninstallRefresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadInstalledPrograms();
        }

        private async Task<List<InstalledProgram>> GetProgramsForExportAsync() => _installedPrograms.Count > 0
            ? _installedPrograms
            : await Task.Run(() => InstalledProgramsService.GetInstalledPrograms());

        private async void UninstallExportTxt_Click(object sender, RoutedEventArgs e)
        {
            var programs = await GetProgramsForExportAsync();
            bool en = Localization.CurrentLanguage == "en";
            if (!await ConfirmProgramExportAsync(programs.Count, "TXT")) return;
            if (await ReportExportService.SaveTextAsync(this, $"WinVora-Programmliste-{DateTime.Now:yyyyMMdd}", ProgramListExporter.ToText(programs, en)))
                ShowInfo(en ? "Program list exported." : "Programmliste wurde exportiert.", InfoBarSeverity.Success);
        }

        private async void UninstallExportCsv_Click(object sender, RoutedEventArgs e)
        {
            var programs = await GetProgramsForExportAsync();
            bool en = Localization.CurrentLanguage == "en";
            char separator = sender is MenuFlyoutItem item && item.Tag?.ToString() == ";" ? ';' : ',';
            if (!await ConfirmProgramExportAsync(programs.Count, separator == ';' ? "CSV (Excel/Semikolon)" : "CSV (Komma)")) return;
            if (await ReportExportService.SaveCsvAsync(this, $"WinVora-Programmliste-{DateTime.Now:yyyyMMdd}", ProgramListExporter.ToCsv(programs, en, separator)))
                ShowInfo(en ? "CSV program list exported." : "CSV-Programmliste wurde exportiert.", InfoBarSeverity.Success);
        }

        private async Task<bool> ConfirmProgramExportAsync(int count, string format)
        {
            bool en = Localization.CurrentLanguage == "en";
            var dialog = new ContentDialog
            {
                XamlRoot = RootGrid.XamlRoot,
                Title = en ? "Export program list?" : "Programmliste exportieren?",
                Content = new TextBlock
                {
                    Text = $"{(en ? "Programs" : "Programme")}: {count}\nFormat: {format}\n" +
                           (en ? "Fields: Name, version, publisher, size, install date" : "Felder: Name, Version, Herausgeber, Größe, Installationsdatum"),
                    TextWrapping = TextWrapping.Wrap
                },
                PrimaryButtonText = en ? "Export" : "Exportieren",
                CloseButtonText = en ? "Cancel" : "Abbrechen",
                DefaultButton = ContentDialogButton.Primary
            };
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }

        private async Task LoadInstalledPrograms()
        {
            if (_isLoadingPrograms) return;
            _isLoadingPrograms = true;
            UninstallPanel.Children.Clear();
            UninstallPanel.Children.Add(new TextBlock
            {
                Text = Localization.CurrentLanguage == "en" ? "Loading installed programs..." : "Installierte Programme werden geladen...",
                Foreground = (SolidColorBrush)RootGrid.Resources["AppMutedForegroundBrush"],
                Margin = new Thickness(4, 16, 0, 8)
            });
            UninstallSearchBox.Text = "";

            UninstallRefreshButton.IsEnabled = false;
            UpdatesLoadingRing.Visibility = Visibility.Visible;
            UpdatesLoadingRing.IsActive = true;

            try
            {
                _installedPrograms = await Task.Run(() => InstalledProgramsService.GetInstalledPrograms());
            }
            catch (Exception ex)
            {
                Logger.LogError("LoadInstalledPrograms", ex);
                UninstallPanel.Children.Add(new TextBlock
                {
                    Text = $"Fehler beim Laden der installierten Programme: {ex.Message}",
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.OrangeRed)
                });
                return;
            }
            finally
            {
                _isLoadingPrograms = false;
                UpdatesLoadingRing.IsActive = false;
                UpdatesLoadingRing.Visibility = Visibility.Collapsed;
                UninstallRefreshButton.IsEnabled = true;
            }

            PageSubtitle.Text = Localization.CurrentLanguage == "en"
                ? $"{_installedPrograms.Count} programs found"
                : $"{_installedPrograms.Count} Programme gefunden";
            UninstallPanel.Children.Clear();

            if (_installedPrograms.Count == 0)
            {
                UninstallPanel.Children.Add(new TextBlock
                {
                    Text = "Keine installierten Programme gefunden.",
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray)
                });
                return;
            }

            foreach (var program in _installedPrograms)
            {
                var card = MakeUninstallCard(program);
                UninstallPanel.Children.Add(card);

                // Icon im Hintergrund nachladen (Extraktion kostet etwas Zeit),
                // Karte erscheint sofort mit Platzhalter-Icon.
                _ = LoadCardIconAsync(card, program.IconPath);
            }

            _uninstallNoResultsText = new TextBlock
            {
                Text = Localization.CurrentLanguage == "en"
                    ? "No programs match your search."
                    : "Keine Programme passen zu deiner Suche.",
                FontSize = 14,
                Foreground = (SolidColorBrush)RootGrid.Resources["AppFaintForegroundBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 20, 0, 0),
                Visibility = Visibility.Collapsed
            };
            UninstallPanel.Children.Add(_uninstallNoResultsText);
        }

        private ToolkitControls.SettingsCard MakeUninstallCard(InstalledProgram program)
        {
            bool en = Localization.CurrentLanguage == "en";

            var detailParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(program.Version)) detailParts.Add($"Version {program.Version}");
            if (!string.IsNullOrWhiteSpace(program.InstallDate))
                detailParts.Add(en ? $"installed on {program.InstallDate}" : $"installiert am {program.InstallDate}");
            if (!string.IsNullOrWhiteSpace(program.SizeDisplay)) detailParts.Add(program.SizeDisplay);

            var uninstallButton = new Button { Content = Localization.T("Nav.Uninstall") };
            var normalBackground = uninstallButton.Background;
            uninstallButton.PointerEntered += (_, __) =>
                uninstallButton.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x35, 0xFF, 0x6B, 0x6B));
            uninstallButton.PointerExited += (_, __) => uninstallButton.Background = normalBackground;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(uninstallButton,
                en ? $"Uninstall {program.DisplayName}" : $"{program.DisplayName} deinstallieren");
            uninstallButton.Click += async (_, __) => await UninstallProgramAsync(program, uninstallButton);

            var card = new ToolkitControls.SettingsCard
            {
                Header = program.DisplayName,
                Description = $"{(en ? "PUBLISHER" : "HERAUSGEBER")}   {program.Publisher}\n" +
                              $"{(en ? "DETAILS" : "DETAILS")}   {string.Join("   ·   ", detailParts)}",
                HeaderIcon = new FontIcon { Glyph = "\uE7B8" }, // Platzhalter, bis echtes Icon geladen ist
                Content = uninstallButton,
                Tag = program.DisplayName // für die Suche/Filterung
            };
            card.MinHeight = 108;
            ToolTipService.SetToolTip(card, program.DisplayName);

            return card;
        }

        // Lädt asynchron das echte App-Icon nach und ersetzt den Platzhalter, falls gefunden.
        private async Task LoadCardIconAsync(ToolkitControls.SettingsCard card, string iconPath)
        {
            if (string.IsNullOrWhiteSpace(iconPath)) return;

            try
            {
                var pngBytes = await Task.Run(() => IconExtractionService.ExtractIconPngBytes(iconPath));
                if (pngBytes == null) return;

                var bitmap = await BytesToBitmapImageAsync(pngBytes);
                if (bitmap == null) return;

                card.HeaderIcon = new ImageIcon { Source = bitmap };
            }
            catch
            {
                // Icon bleibt einfach der Platzhalter
            }
        }

        private async Task<BitmapImage?> BytesToBitmapImageAsync(byte[] pngBytes)
        {
            try
            {
                using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                using (var writer = new Windows.Storage.Streams.DataWriter(stream.GetOutputStreamAt(0)))
                {
                    writer.WriteBytes(pngBytes);
                    await writer.StoreAsync();
                    await writer.FlushAsync();
                }
                stream.Seek(0);

                var bitmap = new BitmapImage();
                await bitmap.SetSourceAsync(stream);
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        private async void UninstallSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _uninstallSearchDebounce?.Cancel();
            var debounce = _uninstallSearchDebounce = new CancellationTokenSource();
            try { await Task.Delay(160, debounce.Token); }
            catch (OperationCanceledException) { return; }
            if (debounce != _uninstallSearchDebounce) return;

            var query = UninstallSearchBox.Text?.Trim() ?? "";
            UninstallClearSearchButton.Visibility = string.IsNullOrEmpty(query) ? Visibility.Collapsed : Visibility.Visible;
            int visibleCount = 0;

            foreach (var child in UninstallPanel.Children)
            {
                if (child is ToolkitControls.SettingsCard card && card.Tag is string name)
                {
                    card.Visibility = string.IsNullOrEmpty(query) ||
                                      name.Contains(query, StringComparison.OrdinalIgnoreCase)
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                    if (card.Visibility == Visibility.Visible) visibleCount++;
                }
            }

            if (_uninstallNoResultsText != null)
                _uninstallNoResultsText.Visibility = visibleCount == 0 && !string.IsNullOrEmpty(query)
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            bool en = Localization.CurrentLanguage == "en";
            PageSubtitle.Text = string.IsNullOrEmpty(query)
                ? (en ? $"{_installedPrograms.Count} programs found" : $"{_installedPrograms.Count} Programme gefunden")
                : (en
                    ? $"Showing {visibleCount} of {_installedPrograms.Count} programs"
                    : $"{visibleCount} von {_installedPrograms.Count} Programmen angezeigt");
        }

        private void UninstallClearSearch_Click(object sender, RoutedEventArgs e)
        {
            UninstallSearchBox.Text = "";
            UninstallSearchBox.Focus(FocusState.Programmatic);
        }

        private async Task UninstallProgramAsync(InstalledProgram program, Button sourceButton)
        {
            if (_isUninstalling) return;
            bool confirmed = await ConfirmAsync(
                "Programm deinstallieren?",
                $"\"{program.DisplayName}\" wird deinstalliert. Persönliche Einstellungen, Spielstände oder Programmdaten können dabei verloren gehen. " +
                "Windows oder der Hersteller kann vorher einen Wiederherstellungspunkt anbieten. Sichere wichtige Daten und fahre nur fort, wenn du das Programm wirklich entfernen möchtest.");

            if (!confirmed) return;

            _isUninstalling = true;
            sourceButton.IsEnabled = false;
            UninstallRefreshButton.IsEnabled = false;

            bool en = Localization.CurrentLanguage == "en";
            UninstallStatusPanel.Visibility = Visibility.Visible;
            UninstallStatusRing.IsActive = true;
            UninstallStatusRing.Visibility = Visibility.Visible;
            UninstallStatusIcon.Visibility = Visibility.Collapsed;
            UninstallStatusText.Text = en ? $"Preparing {program.DisplayName}..." : $"{program.DisplayName} wird vorbereitet...";
            UninstallStatusDetailText.Text = en ? "Waiting for the program's uninstaller." : "Der Deinstaller des Programms wird aufgerufen.";

            var (success, message) = await Task.Run(() => InstalledProgramsService.Uninstall(program));
            Logger.Log($"Deinstallation '{program.DisplayName}': {(success ? "gestartet" : "Fehler")} - {message}");

            UninstallStatusRing.IsActive = false;
            UninstallStatusRing.Visibility = Visibility.Collapsed;
            UninstallStatusIcon.Visibility = Visibility.Visible;
            UninstallStatusIcon.Glyph = success ? "\uE73E" : "\uEA39";
            UninstallStatusIcon.Foreground = new SolidColorBrush(success
                ? Windows.UI.Color.FromArgb(0xFF, 0x4C, 0xD9, 0x73)
                : Windows.UI.Color.FromArgb(0xFF, 0xFF, 0x6B, 0x6B));
            UninstallStatusText.Text = success
                ? (en ? "Uninstaller started" : "Deinstaller gestartet")
                : (en ? "Uninstall could not be started" : "Deinstallation konnte nicht gestartet werden");
            UninstallStatusDetailText.Text = message;

            if (success)
            {
                LogActivity("\uE74D",
                    $"Deinstaller für {program.DisplayName} gestartet",
                    $"Uninstaller for {program.DisplayName} started");
                ScheduleDashboardRefresh();
                if (_settings.OfferUninstallLeftoverScan)
                {
                    await Task.Delay(1200);
                    var leftovers = await Task.Run(() => InstalledProgramsService.FindPotentialLeftovers(program));
                    if (leftovers.Count > 0)
                        await ShowUninstallLeftoversAsync(program.DisplayName, leftovers);
                }
            }
            else
            {
                LogActivity("\uEA39",
                    $"Deinstallation von {program.DisplayName} konnte nicht gestartet werden",
                    $"Could not start uninstalling {program.DisplayName}",
                    "Failed");
            }

            sourceButton.IsEnabled = true;
            UninstallRefreshButton.IsEnabled = true;
            _isUninstalling = false;
            if (success)
            {
                await Task.Delay(4000);
                if (_currentPageKey == "Uninstall")
                    UninstallStatusPanel.Visibility = Visibility.Collapsed;
            }
        }

        private async Task ShowUninstallLeftoversAsync(string programName, List<string> leftovers)
        {
            bool en = Localization.CurrentLanguage == "en";
            var panel = new StackPanel { Spacing = 8, MaxWidth = 620 };
            panel.Children.Add(new TextBlock
            {
                Text = en
                    ? "Nothing is deleted automatically. Review each possible leftover."
                    : "Es wird nichts automatisch gelöscht. Prüfe jeden möglichen Rest einzeln.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = (SolidColorBrush)RootGrid.Resources["AppMutedForegroundBrush"]
            });
            foreach (string leftover in leftovers)
            {
                string value = leftover.Contains(':') ? leftover[(leftover.IndexOf(':') + 1)..].Trim() : leftover;
                var row = new Grid { ColumnSpacing = 8 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new TextBlock { Text = leftover, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
                if (leftover.StartsWith("Ordner:", StringComparison.OrdinalIgnoreCase) && Directory.Exists(value))
                {
                    var open = new Button { Content = en ? "Open" : "Öffnen" };
                    open.Click += (_, __) => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{value}\"") { UseShellExecute = true });
                    Grid.SetColumn(open, 1); row.Children.Add(open);
                }
                var copy = new Button { Content = en ? "Copy" : "Kopieren" };
                copy.Click += (_, __) =>
                {
                    var data = new Windows.ApplicationModel.DataTransfer.DataPackage();
                    data.SetText(value);
                    Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
                };
                Grid.SetColumn(copy, 2); row.Children.Add(copy);
                panel.Children.Add(new Border
                {
                    Padding = new Thickness(10), CornerRadius = new CornerRadius(8),
                    Background = (SolidColorBrush)RootGrid.Resources["AppOverlay18"], Child = row
                });
            }
            var dialog = new ContentDialog
            {
                XamlRoot = RootGrid.XamlRoot,
                Title = en ? $"Possible leftovers from {programName}" : $"Mögliche Reste von {programName}",
                Content = new ScrollViewer { Content = panel, MaxHeight = 430 },
                PrimaryButtonText = en ? "Check again" : "Erneut prüfen",
                CloseButtonText = en ? "Close" : "Schließen"
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                var program = _installedPrograms.FirstOrDefault(item =>
                    item.DisplayName.Equals(programName, StringComparison.OrdinalIgnoreCase));
                if (program != null)
                {
                    var refreshed = await Task.Run(() => InstalledProgramsService.FindPotentialLeftovers(program));
                    if (refreshed.Count == 0)
                        ShowInfo(en ? "No leftovers were found." : "Es wurden keine Reste mehr gefunden.", InfoBarSeverity.Success);
                    else
                        await ShowUninstallLeftoversAsync(programName, refreshed);
                }
            }
        }
    }
}
