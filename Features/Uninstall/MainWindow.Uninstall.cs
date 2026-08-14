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
        private readonly List<(InstalledProgram Program, CheckBox Selection, Button Button)> _uninstallRows = new();
        private HashSet<string> _uninstallSelectionRestore = new(StringComparer.OrdinalIgnoreCase);

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
            _uninstallSelectionRestore = _uninstallRows
                .Where(row => row.Selection.IsChecked == true)
                .Select(row => row.Program.DisplayName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            _isLoadingPrograms = true;
            UninstallPanel.Children.Clear();
            _uninstallRows.Clear();
            UpdateUninstallSelectionAction();
            UninstallPanel.Children.Add(LoadingStateUiBuilder.Create(RootGrid.Resources, 4, !_settings.ReducedMotion));
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
                    Foreground = (SolidColorBrush)RootGrid.Resources["AppErrorBrush"]
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
                    Foreground = (SolidColorBrush)RootGrid.Resources["AppMutedForegroundBrush"]
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

            var selection = new CheckBox
            {
                VerticalAlignment = VerticalAlignment.Center,
                IsChecked = _uninstallSelectionRestore.Contains(program.DisplayName)
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(selection,
                en ? $"Select {program.DisplayName}" : $"{program.DisplayName} auswählen");
            var uninstallButton = new Button
            {
                Content = Localization.T("Nav.Uninstall"),
                IsEnabled = false,
                CornerRadius = new CornerRadius(8),
                UseLayoutRounding = true,
                BorderThickness = new Thickness(0)
            };
            var normalBackground = uninstallButton.Background;
            var dangerBackground = (SolidColorBrush)RootGrid.Resources["AppDangerSurfaceBrush"];
            uninstallButton.Resources["ButtonBackgroundPointerOver"] = dangerBackground;
            uninstallButton.Resources["ButtonBackgroundPressed"] = dangerBackground;
            uninstallButton.Resources["ButtonBorderBrushPointerOver"] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            uninstallButton.Resources["ButtonBorderBrushPressed"] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(uninstallButton,
                en ? $"Uninstall {program.DisplayName}" : $"{program.DisplayName} deinstallieren");
            uninstallButton.Click += async (_, __) => await UninstallProgramAsync(program, uninstallButton);

            selection.Checked += (_, __) =>
            {
                uninstallButton.IsEnabled = true;
                uninstallButton.Foreground = (SolidColorBrush)RootGrid.Resources["AppErrorBrush"];
                uninstallButton.Background = dangerBackground;
                UpdateUninstallSelectionAction();
            };
            selection.Unchecked += (_, __) =>
            {
                uninstallButton.IsEnabled = false;
                uninstallButton.ClearValue(Button.ForegroundProperty);
                uninstallButton.Background = normalBackground;
                UpdateUninstallSelectionAction();
            };

            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                VerticalAlignment = VerticalAlignment.Center
            };
            actions.Children.Add(selection);
            actions.Children.Add(uninstallButton);

            var card = new ToolkitControls.SettingsCard
            {
                Header = program.DisplayName,
                Description = $"{(en ? "PUBLISHER" : "HERAUSGEBER")}   {program.Publisher}\n" +
                              $"{(en ? "DETAILS" : "DETAILS")}   {string.Join("   ·   ", detailParts)}",
                HeaderIcon = new FontIcon { Glyph = "\uE7B8", FontSize = 28, Width = 34, Height = 34 },
                Content = actions,
                Background = (SolidColorBrush)RootGrid.Resources["AppCardSurfaceBrush"],
                BorderThickness = new Thickness(0),
                Tag = program.DisplayName // für die Suche/Filterung
            };
            card.MinHeight = 86;
            card.Padding = new Thickness(12, 8, 12, 8);
            ToolTipService.SetToolTip(card, program.DisplayName);
            _uninstallRows.Add((program, selection, uninstallButton));

            return card;
        }

        private void UpdateUninstallSelectionAction()
        {
            int count = _uninstallRows.Count(row => row.Selection.IsChecked == true);
            bool en = Localization.CurrentLanguage == "en";
            UninstallSelectedButton.Content = count > 0
                ? (en ? $"Uninstall {count} selected" : $"{count} ausgewählte deinstallieren")
                : (en ? "Uninstall selected" : "Ausgewählte deinstallieren");
            UninstallSelectedButton.IsEnabled = count > 0 && !_isUninstalling;
            UninstallSelectedButton.Style = count > 0
                ? (Style)Application.Current.Resources["AccentButtonStyle"]
                : null;
        }

        private async void UninstallSelected_Click(object sender, RoutedEventArgs e)
        {
            if (_isUninstalling) return;
            var selected = _uninstallRows
                .Where(row => row.Selection.IsChecked == true)
                .Select(row => row.Program)
                .ToList();
            if (selected.Count == 0) return;

            bool en = Localization.CurrentLanguage == "en";
            var confirmation = CommonUiBuilder.CreateConfirmation(
                RootGrid.XamlRoot,
                en ? "Uninstall " + selected.Count + " programs?" : selected.Count + " Programme deinstallieren?",
                en
                    ? "WinVora starts every publisher uninstaller separately. Program data and settings may be removed. Review each uninstall wizard before continuing."
                    : "WinVora startet jeden Hersteller-Deinstaller einzeln. Programmdaten und Einstellungen können entfernt werden. Prüfe jeden Assistenten vor dem Fortfahren.",
                en ? "Start" : "Starten",
                en ? "Cancel" : "Abbrechen");
            if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;

            _isUninstalling = true;
            UpdateUninstallSelectionAction();
            UninstallRefreshButton.IsEnabled = false;
            int started = 0;
            int failed = 0;
            int skipped = 0;
            int confirmedRemoved = 0;

            for (int index = 0; index < selected.Count; index++)
            {
                var program = selected[index];
                var itemDialog = new ContentDialog
                {
                    XamlRoot = RootGrid.XamlRoot,
                    Title = en ? "Next: " + program.DisplayName : "Als Nächstes: " + program.DisplayName,
                    Content = en
                        ? "Start this program's uninstaller, skip it, or stop the remaining queue."
                        : "Starte den Deinstaller dieses Programms, überspringe es oder beende die restliche Warteschlange.",
                    PrimaryButtonText = en ? "Uninstall" : "Deinstallieren",
                    SecondaryButtonText = en ? "Skip" : "Überspringen",
                    CloseButtonText = en ? "Stop queue" : "Warteschlange beenden",
                    DefaultButton = ContentDialogButton.Primary
                };
                var itemChoice = await itemDialog.ShowAsync();
                if (itemChoice == ContentDialogResult.None) break;
                if (itemChoice == ContentDialogResult.Secondary)
                {
                    skipped++;
                    continue;
                }
                UninstallStatusPanel.Visibility = Visibility.Visible;
                UninstallStatusRing.IsActive = true;
                UninstallStatusRing.Visibility = Visibility.Visible;
                UninstallStatusIcon.Visibility = Visibility.Collapsed;
                UninstallStatusText.Text = en
                    ? "Starting " + program.DisplayName + " (" + (index + 1) + "/" + selected.Count + ")"
                    : "Starte " + program.DisplayName + " (" + (index + 1) + "/" + selected.Count + ")";
                UninstallStatusDetailText.Text = en
                    ? "Waiting for the publisher uninstaller."
                    : "Warte auf den Hersteller-Deinstaller.";

                var result = await Task.Run(() => InstalledProgramsService.Uninstall(program));
                if (result.success) started++; else failed++;
                LogActivity(
                    result.success ? "\uE74D" : "\uEA39",
                    result.success ? "Deinstaller für " + program.DisplayName + " gestartet" : "Deinstallation von " + program.DisplayName + " fehlgeschlagen",
                    result.success ? "Uninstaller for " + program.DisplayName + " started" : "Uninstalling " + program.DisplayName + " failed",
                    result.success ? "Successful" : "Failed");

                if (result.success && index < selected.Count - 1)
                {
                    var nextDialog = CommonUiBuilder.CreateConfirmation(
                        RootGrid.XamlRoot,
                        en ? "Continue with the next program?" : "Mit dem nächsten Programm fortfahren?",
                        en
                            ? "Finish the current uninstall wizard first. WinVora waits for your confirmation so multiple uninstallers do not run simultaneously."
                            : "Schließe zuerst den aktuellen Deinstallationsassistenten ab. WinVora wartet auf deine Bestätigung, damit nicht mehrere Deinstaller gleichzeitig laufen.",
                        en ? "Start next" : "Nächstes starten",
                        en ? "Stop queue" : "Warteschlange beenden");
                    if (await nextDialog.ShowAsync() != ContentDialogResult.Primary) break;
                }
                else if (result.success)
                {
                    var finishedDialog = CommonUiBuilder.CreateConfirmation(
                        RootGrid.XamlRoot,
                        en ? "Finish the uninstall" : "Deinstallation abschließen",
                        en
                            ? "Complete and close the publisher uninstall wizard, then let WinVora verify the result."
                            : "Schließe den Hersteller-Assistenten vollständig ab. Danach prüft WinVora das Ergebnis.",
                        en ? "Verify" : "Prüfen",
                        en ? "Skip verification" : "Prüfung überspringen");
                    if (await finishedDialog.ShowAsync() != ContentDialogResult.Primary)
                        continue;
                }
                if (result.success && await IsProgramRemovedAsync(program))
                    confirmedRemoved++;
            }

            _isUninstalling = false;
            UninstallRefreshButton.IsEnabled = true;
            UninstallStatusRing.IsActive = false;
            UninstallStatusRing.Visibility = Visibility.Collapsed;
            UninstallStatusIcon.Visibility = Visibility.Visible;
            UninstallStatusIcon.Glyph = failed == 0 ? "\uE73E" : "\uEA39";
            UninstallStatusText.Text = en
                ? started + " started · " + confirmedRemoved + " removed · " + skipped + " skipped · " + failed + " failed"
                : started + " gestartet · " + confirmedRemoved + " entfernt · " + skipped + " übersprungen · " + failed + " fehlgeschlagen";
            UninstallStatusDetailText.Text = en
                ? "The program list is being refreshed."
                : "Die Programmliste wird aktualisiert.";
            await LoadInstalledPrograms();
        }

        private static async Task<bool> IsProgramRemovedAsync(InstalledProgram program, IProgress<int>? countdown = null)
        {
            for (int attempt = 0; attempt < 6; attempt++)
            {
                countdown?.Report(6 - attempt);
                await Task.Delay(1000);
                var programs = await Task.Run(() => InstalledProgramsService.GetInstalledPrograms());
                if (!programs.Any(item =>
                    item.DisplayName.Equals(program.DisplayName, StringComparison.OrdinalIgnoreCase) &&
                    item.UninstallString.Equals(program.UninstallString, StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
            return false;
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

                card.HeaderIcon = new ImageIcon { Source = bitmap, Width = 34, Height = 34 };
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

                var finishedDialog = CommonUiBuilder.CreateConfirmation(
                    RootGrid.XamlRoot,
                    en ? "Finish the uninstall" : "Deinstallation abschließen",
                    en
                        ? "Complete and close the publisher uninstall wizard. WinVora can then verify that the program was removed."
                        : "Schließe den Hersteller-Deinstaller vollständig. Danach kann WinVora prüfen, ob das Programm entfernt wurde.",
                    en ? "Verify now" : "Jetzt prüfen",
                    en ? "Later" : "Später");
                bool verify = await finishedDialog.ShowAsync() == ContentDialogResult.Primary;
                bool removed = false;
                if (verify)
                {
                    UninstallStatusRing.IsActive = true;
                    UninstallStatusRing.Visibility = Visibility.Visible;
                    UninstallStatusIcon.Visibility = Visibility.Collapsed;
                    UninstallStatusText.Text = en ? "Verifying uninstall..." : "Deinstallation wird geprüft...";
                    UninstallStatusDetailText.Text = en ? "Refreshing the installed program registry." : "Die Liste installierter Programme wird neu eingelesen.";
                    var countdown = new Progress<int>(seconds =>
                    {
                        UninstallStatusDetailText.Text = en
                            ? $"Refreshing the program registry · {seconds}s remaining"
                            : $"Programmliste wird geprüft · noch {seconds} s";
                    });
                    removed = await IsProgramRemovedAsync(program, countdown);
                    await LoadInstalledPrograms();
                    if (removed)
                    {
                        ShowInfo(en ? $"{program.DisplayName} was removed." : $"{program.DisplayName} wurde entfernt.", InfoBarSeverity.Success);
                        if (_settings.OfferUninstallLeftoverScan)
                        {
                            var leftovers = await Task.Run(() => InstalledProgramsService.FindPotentialLeftovers(program));
                            if (leftovers.Count > 0)
                                await ShowUninstallLeftoversAsync(program.DisplayName, leftovers);
                        }
                    }
                    else
                    {
                        OfferUninstallRecheck(program);
                    }
                }
                else if (_currentPageKey == "Uninstall")
                    await LoadInstalledPrograms();
            }
            else
            {
                LogActivity("\uEA39",
                    $"Deinstallation von {program.DisplayName} konnte nicht gestartet werden",
                    $"Could not start uninstalling {program.DisplayName}",
                    "Failed");
            }

            if (sourceButton.XamlRoot != null)
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

        private void OfferUninstallRecheck(InstalledProgram program)
        {
            bool en = Localization.CurrentLanguage == "en";
            ShowInfo(en
                ? "The program is still registered. Finish the uninstaller and check again."
                : "Das Programm ist noch registriert. Schließe den Deinstaller und prüfe erneut.",
                InfoBarSeverity.Warning);
            var retry = new Button { Content = en ? "Check again" : "Erneut prüfen" };
            retry.Click += async (_, __) =>
            {
                retry.IsEnabled = false;
                UninstallStatusPanel.Visibility = Visibility.Visible;
                UninstallStatusRing.IsActive = true;
                UninstallStatusRing.Visibility = Visibility.Visible;
                var countdown = new Progress<int>(seconds =>
                {
                    UninstallStatusText.Text = en ? "Verifying uninstall..." : "Deinstallation wird geprüft...";
                    UninstallStatusDetailText.Text = en ? $"{seconds}s remaining" : $"Noch {seconds} s";
                });
                bool removed = await IsProgramRemovedAsync(program, countdown);
                await LoadInstalledPrograms();
                if (removed)
                {
                    AppInfoBar.IsOpen = false;
                    AppInfoBar.ActionButton = null;
                    ShowInfo(en ? $"{program.DisplayName} was removed." : $"{program.DisplayName} wurde entfernt.", InfoBarSeverity.Success);
                }
                else
                {
                    OfferUninstallRecheck(program);
                }
            };
            AppInfoBar.ActionButton = retry;
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
                    open.Click += (_, __) =>
                    {
                        var result = ExplorerService.OpenFolder(value);
                        if (result == ExplorerOpenResult.Missing)
                            ShowInfo(en ? "The folder no longer exists." : "Der Ordner ist nicht mehr vorhanden.", InfoBarSeverity.Warning);
                        else if (result == ExplorerOpenResult.Failed)
                            ShowInfo(en ? "The folder could not be opened." : "Der Ordner konnte nicht geöffnet werden.", InfoBarSeverity.Error);
                    };
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
