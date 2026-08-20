using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace WinVora
{
    public sealed partial class MainWindow
    {
        private Window? _settingsWindow;

        // Baut eine Einstellungs-Karte mit Überschrift und liefert das
        // StackPanel zurück, in das die eigentlichen Controls kommen -
        // vermeidet die Wiederholung von Border/Padding/Farben pro Karte.
        private Border MakeSettingsCard(string title, out StackPanel content)
        {
            var card = SettingsUiBuilder.CreateSection(title, RootGrid.Resources, out content);
            AttachCardHoverEffect(card);
            return card;
        }

        // Kleines Label+Control-Paar (z.B. für ComboBoxen mit Beschriftung).
        private static void PreventClosedComboBoxWheelChange(ComboBox comboBox)
        {
            comboBox.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler((_, args) =>
            {
                if (!comboBox.IsDropDownOpen)
                    args.Handled = true;
            }), handledEventsToo: true);
        }

        private StackPanel MakeLabeledControl(string label, FrameworkElement control)
            => SettingsUiBuilder.CreateLabeledControl(label, control, RootGrid.Resources);

        // Wendet die gleiche dunkle Titelleiste wie beim Hauptfenster auch auf
        // Popup-Fenster (Einstellungen, Changelog) an - sonst zeigen die die
        // weiße Windows-Standardleiste, obwohl der Rest der App dunkel ist.
        private void StyleDarkWindow(Window window, int width, int height)
        {
            window.ExtendsContentIntoTitleBar = true;
            window.AppWindow.Resize(new Windows.Graphics.SizeInt32(width, height));

            // Gleicher Mica-Effekt wie im Hauptfenster, damit Einstellungen-
            // und Changelog-Fenster optisch dazu passen statt flach zu wirken.
            if (_settings.UseMica && Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported())
            {
                window.SystemBackdrop = new MicaBackdrop { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.BaseAlt };
            }

            ApplySecondaryWindowTitleBarColors(window, _isDarkTheme);
        }

        private static void ApplySecondaryWindowTitleBarColors(Window window, bool dark)
        {
            var titleBar = window.AppWindow.TitleBar;
            var fg = dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
            byte rgb = dark ? (byte)0xFF : (byte)0x00;

            titleBar.BackgroundColor = Microsoft.UI.Colors.Transparent;
            titleBar.InactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
            titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
            titleBar.ButtonForegroundColor = fg;
            titleBar.ButtonInactiveForegroundColor = Microsoft.UI.ColorHelper.FromArgb(0x80, rgb, rgb, rgb);
            titleBar.ButtonHoverBackgroundColor = Microsoft.UI.ColorHelper.FromArgb(0x30, rgb, rgb, rgb);
            titleBar.ButtonHoverForegroundColor = fg;
            titleBar.ButtonPressedBackgroundColor = Microsoft.UI.ColorHelper.FromArgb(0x50, rgb, rgb, rgb);
            titleBar.ButtonPressedForegroundColor = fg;
            titleBar.IconShowOptions = Microsoft.UI.Windowing.IconShowOptions.HideIconAndSystemMenu;

            // Keine eigene vollbreite Drag-Fläche setzen. WinUI reserviert den
            // rechten Caption-Bereich selbst zuverlässig für Minimieren,
            // Maximieren und Schließen – auch bei Skalierung und Themewechsel.
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            bool en = Localization.CurrentLanguage == "en";
            if (_settingsWindow != null)
            {
                _settingsWindow.Activate();
                WindowActivationService.ShowOwnedInFront(this, _settingsWindow);
                return;
            }

            _settingsWindow = new Window { Title = Localization.T("Settings.WindowTitle") };
            var settingsWindow = _settingsWindow;
            settingsWindow.Closed += (_, __) =>
            {
                SaveSecondaryWindowPlacement(settingsWindow, settingsWindow: true);
                _settingsWindow = null;
            };

            var root = new Grid
            {
                Background = (SolidColorBrush)RootGrid.Resources["AppRootBackgroundBrush"],
                UseLayoutRounding = true
            };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RequestedTheme = _isDarkTheme ? ElementTheme.Dark : ElementTheme.Light;

            var panel = new StackPanel
            {
                Spacing = 18,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            var settingsHeader = new Grid { ColumnSpacing = 14, Margin = new Thickness(0, 2, 0, 4) };
            settingsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            settingsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            settingsHeader.Children.Add(new Border
            {
                Width = 46,
                Height = 46,
                CornerRadius = new CornerRadius(13),
                Background = (SolidColorBrush)RootGrid.Resources["AppAccentOverlay20"],
                Child = new FontIcon { Glyph = "\uE713", FontSize = 21, Foreground = (SolidColorBrush)RootGrid.Resources["AppAccentBrushLight"] }
            });
            var settingsHeading = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            settingsHeading.Children.Add(new TextBlock
            {
                Text = Localization.T("Settings.Title"),
                FontSize = 28,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });
            settingsHeading.Children.Add(new TextBlock
            {
                Text = Localization.CurrentLanguage == "en" ? "Customize WinVora to suit you" : "WinVora nach deinen Wünschen anpassen",
                FontSize = 12,
                Foreground = (SolidColorBrush)RootGrid.Resources["AppMutedForegroundBrush"]
            });
            Grid.SetColumn(settingsHeading, 1);
            settingsHeader.Children.Add(settingsHeading);
            panel.Children.Add(settingsHeader);

            var settingsSearchBox = new TextBox
            {
                PlaceholderText = Localization.CurrentLanguage == "en" ? "Search settings..." : "Einstellungen durchsuchen...",
                Height = 42,
                Padding = new Thickness(12, 9, 12, 0),
                VerticalContentAlignment = VerticalAlignment.Stretch,
                CornerRadius = new CornerRadius(10),
                Background = (SolidColorBrush)RootGrid.Resources["AppOverlay18"],
                BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0)
            };
            settingsSearchBox.Resources["TextControlBorderBrushFocused"] =
                (SolidColorBrush)RootGrid.Resources["AppAccentBrushLight"];
            settingsSearchBox.Resources["TextControlBorderBrushPointerOver"] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            settingsSearchBox.Resources["TextControlBorderBrush"] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(settingsSearchBox,
                Localization.CurrentLanguage == "en" ? "Search settings" : "Einstellungen durchsuchen");
            var noSettingsResults = new TextBlock
            {
                Text = Localization.CurrentLanguage == "en" ? "No settings found." : "Keine Einstellungen gefunden.",
                Foreground = (SolidColorBrush)RootGrid.Resources["AppFaintForegroundBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 8, 0, 8),
                Visibility = Visibility.Collapsed
            };
            var clearSettingsSearch = new Button
            {
                Content = new FontIcon { Glyph = "\uE711", FontSize = 12 },
                Width = 34,
                Height = 34,
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0),
                Visibility = Visibility.Collapsed
            };
            ToolTipService.SetToolTip(clearSettingsSearch,
                Localization.CurrentLanguage == "en" ? "Clear search" : "Suche leeren");
            var highlightedText = new Dictionary<TextBlock, (Brush? Foreground, Windows.UI.Text.FontWeight Weight)>();
            void HighlightSearchMatches(DependencyObject rootElement, string query)
            {
                int childCount = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(rootElement);
                for (int index = 0; index < childCount; index++)
                {
                    var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(rootElement, index);
                    if (child is TextBlock textBlock)
                    {
                        if (!highlightedText.ContainsKey(textBlock))
                            highlightedText[textBlock] = (textBlock.Foreground, textBlock.FontWeight);
                        bool match = !string.IsNullOrWhiteSpace(query) &&
                            textBlock.Text.Contains(query, StringComparison.CurrentCultureIgnoreCase);
                        textBlock.Foreground = match
                            ? (SolidColorBrush)RootGrid.Resources["AppAccentBrushLight"]
                            : highlightedText[textBlock].Foreground;
                        textBlock.FontWeight = match
                            ? Microsoft.UI.Text.FontWeights.SemiBold
                            : highlightedText[textBlock].Weight;
                    }
                    HighlightSearchMatches(child, query);
                }
            }
            clearSettingsSearch.Click += (_, __) =>
            {
                settingsSearchBox.Text = "";
                settingsSearchBox.Focus(FocusState.Programmatic);
            };
            settingsSearchBox.TextChanged += (_, __) =>
            {
                string query = settingsSearchBox.Text.Trim();
                clearSettingsSearch.Visibility = string.IsNullOrWhiteSpace(query)
                    ? Visibility.Collapsed
                    : Visibility.Visible;
                int visibleCards = 0;
                foreach (var settingsCard in panel.Children.OfType<Border>().Where(border => border.Tag is string))
                {
                    string searchable = UiTextSearch.Collect(settingsCard);
                    bool matches = string.IsNullOrWhiteSpace(query) ||
                        searchable.Contains(query, StringComparison.CurrentCultureIgnoreCase);
                    settingsCard.Visibility = matches
                            ? Visibility.Visible
                            : Visibility.Collapsed;
                    settingsCard.BorderThickness = !string.IsNullOrWhiteSpace(query) && matches
                        ? new Thickness(1)
                        : new Thickness(0);
                    settingsCard.BorderBrush = !string.IsNullOrWhiteSpace(query) && matches
                        ? (SolidColorBrush)RootGrid.Resources["AppAccentBrushLight"]
                        : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                    if (settingsCard.Visibility == Visibility.Visible) visibleCards++;
                }
                noSettingsResults.Visibility = visibleCards == 0 ? Visibility.Visible : Visibility.Collapsed;
                HighlightSearchMatches(panel, query);
            };
            var settingsSearchHost = new Grid();
            settingsSearchHost.Children.Add(settingsSearchBox);
            settingsSearchHost.Children.Add(clearSettingsSearch);
            panel.Children.Add(settingsSearchHost);
            panel.Children.Add(noSettingsResults);

            // ---- Auto-Update (ganz oben, damit ein verfügbares Update sofort
            //      ins Auge fällt statt unten in der Wartung versteckt zu sein) ----
            var updateCard = MakeSettingsCard(Localization.T("Settings.UpdateSection"), out var updateContent);
            bool updateUiEnglish = Localization.CurrentLanguage == "en";

            var updateChannelCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            updateChannelCombo.Items.Add(new ComboBoxItem
            {
                Content = updateUiEnglish ? "Stable releases" : "Stabile Versionen",
                Tag = "Stable"
            });
            updateChannelCombo.Items.Add(new ComboBoxItem
            {
                Content = updateUiEnglish ? "Beta releases" : "Beta-Versionen",
                Tag = "Beta"
            });
            updateChannelCombo.SelectedIndex = _settings.UpdateChannel == "Beta" ? 1 : 0;
            PreventClosedComboBoxWheelChange(updateChannelCombo);
            updateChannelCombo.SelectionChanged += (_, __) =>
            {
                if (updateChannelCombo.SelectedItem is not ComboBoxItem item) return;
                _settings.UpdateChannel = item.Tag?.ToString() == "Beta" ? "Beta" : "Stable";
                _settings.Save();
                _pendingUpdateInfo = null;
                UpdateUpdateChannelUi();
            };
            updateContent.Children.Add(MakeLabeledControl(
                updateUiEnglish ? "Update channel" : "Updatekanal",
                updateChannelCombo));
            updateContent.Children.Add(new TextBlock
            {
                Text = updateUiEnglish
                    ? "Beta releases may contain unfinished features. You can switch back to stable at any time."
                    : "Beta-Versionen können unfertige Funktionen enthalten. Du kannst jederzeit wieder auf Stabil wechseln.",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (SolidColorBrush)RootGrid.Resources["AppMutedForegroundBrush"]
            });

            var updateStatusText = new TextBlock
            {
                Text = _pendingUpdateInfo != null
                    ? (updateUiEnglish
                        ? $"Version {_pendingUpdateInfo.Version} is available (you have {CurrentVersion})."
                        : $"Version {_pendingUpdateInfo.Version} ist verfügbar (du hast {CurrentVersion}).")
                    : (updateUiEnglish ? $"Current version: {CurrentVersion}" : $"Aktuelle Version: {CurrentVersion}"),
                Foreground = (SolidColorBrush)RootGrid.Resources["AppForegroundBrush"],
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap
            };
            updateContent.Children.Add(updateStatusText);

            var updateProgressBar = new ProgressBar
            {
                Minimum = 0,
                Maximum = 100,
                Visibility = Visibility.Collapsed,
                Foreground = (SolidColorBrush)RootGrid.Resources["AppAccentBrush"]
            };
            updateContent.Children.Add(updateProgressBar);

            var updateButton = new Button
            {
                // Falls der Hintergrund-Check bereits ein Update gefunden hat,
                // direkt zum Aktualisieren einladen statt erneut suchen zu lassen.
                Content = _pendingUpdateInfo != null
                    ? Localization.T("Settings.UpdateNow")
                    : Localization.T("Settings.CheckUpdate"),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Style = _pendingUpdateInfo != null ? (Style)Application.Current.Resources["AccentButtonStyle"] : null
            };
            updateButton.Click += async (_, __) =>
            {
                updateButton.IsEnabled = false;

                UpdateInfo? update = _pendingUpdateInfo;

                if (update == null)
                {
                    updateStatusText.Text = updateUiEnglish ? "Checking for updates..." : "Suche nach Updates...";
                    try
                    {
                        update = await UpdateService.CheckForUpdateAsync(
                            CurrentVersion,
                            _settings.UpdateChannel == "Beta",
                            _startupCancellation.Token);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError("CheckForUpdateAsync", ex);
                        updateStatusText.Text = UpdateErrorMessageService.ForCheck(ex, updateUiEnglish);
                        updateButton.IsEnabled = true;
                        return;
                    }
                }

                if (update == null)
                {
                    updateStatusText.Text = updateUiEnglish
                        ? $"You already have the latest version ({CurrentVersion})."
                        : $"Du hast bereits die neueste Version ({CurrentVersion}).";
                    updateButton.IsEnabled = true;
                    return;
                }

                var confirmed = await ConfirmAsync(
                    updateUiEnglish ? "Update available" : "Update verfügbar",
                    updateUiEnglish
                        ? $"Version {update.Version} is available (you have {CurrentVersion}). WinVora will close and update automatically. Update now?"
                        : $"Version {update.Version} ist verfügbar (du hast {CurrentVersion}). WinVora wird geschlossen und automatisch aktualisiert. Jetzt aktualisieren?",
                    primaryButtonText: updateUiEnglish ? "Update now" : "Jetzt aktualisieren",
                    respectDeleteConfirmationSetting: false,
                    dialogRoot: (settingsWindow.Content as FrameworkElement)?.XamlRoot);

                if (!confirmed)
                {
                    updateStatusText.Text = updateUiEnglish
                        ? $"Update {update.Version} is available but was not installed."
                        : $"Update auf {update.Version} verfügbar, aber nicht installiert.";
                    updateButton.IsEnabled = true;
                    return;
                }

                if (update.IsPrerelease)
                {
                    try
                    {
                        SettingsBackupService.CreateAutomatic(_settings, "before-beta-update");
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError("Beta-Einstellungssicherung", ex);
                        updateStatusText.Text = updateUiEnglish
                            ? "The settings backup failed. The beta update was not started."
                            : "Die Einstellungssicherung ist fehlgeschlagen. Das Beta-Update wurde nicht gestartet.";
                        updateButton.IsEnabled = true;
                        return;
                    }
                }

                updateProgressBar.Visibility = Visibility.Visible;
                updateProgressBar.Value = 0;
                updateStatusText.Text = updateUiEnglish
                    ? $"Downloading version {update.Version}..."
                    : $"Lade Version {update.Version} herunter...";

                var progress = new Progress<DownloadProgressInfo>(info =>
                {
                    double downloadedMb = info.BytesReceived / 1024.0 / 1024.0;

                    if (info.TotalBytes > 0)
                    {
                        double percent = (double)info.BytesReceived / info.TotalBytes * 100;
                        double totalMb = info.TotalBytes / 1024.0 / 1024.0;
                        updateProgressBar.IsIndeterminate = false;
                        updateProgressBar.Value = percent;
                        updateStatusText.Text = updateUiEnglish
                            ? $"Downloading version {update.Version}... ({downloadedMb:0.0} / {totalMb:0.0} MB)"
                            : $"Lade Version {update.Version} herunter... ({downloadedMb:0.0} / {totalMb:0.0} MB)";
                    }
                    else
                    {
                        // Server liefert keine Gesamtgröße - trotzdem sichtbar
                        // machen, dass Daten ankommen, statt einen stehenden Text
                        // zu zeigen, der wie ein Hänger aussieht.
                        updateProgressBar.IsIndeterminate = true;
                        updateStatusText.Text = updateUiEnglish
                            ? $"Downloading version {update.Version}... ({downloadedMb:0.0} MB)"
                            : $"Lade Version {update.Version} herunter... ({downloadedMb:0.0} MB)";
                    }
                });

                try
                {
                    var installerPath = await UpdateService.DownloadUpdateAsync(
                        update,
                        progress,
                        _startupCancellation.Token);
                    Logger.Log($"Update auf Version {update.Version} heruntergeladen, starte Installer.");

                    UpdateService.RunInstaller(installerPath);

                    // App schließt sich selbst, damit der Installer die Dateien
                    // ungehindert überschreiben kann.
                    Application.Current.Exit();
                }
                catch (Exception ex)
                {
                    Logger.LogError("DownloadUpdateAsync/RunInstaller", ex);
                    updateStatusText.Text = UpdateErrorMessageService.ForInstall(ex, updateUiEnglish);
                    updateProgressBar.Visibility = Visibility.Collapsed;
                    updateButton.IsEnabled = true;
                }
            };
            updateContent.Children.Add(updateButton);

            if (IsBetaBuild)
            {
                var betaHubButton = new Button
                {
                    Content = updateUiEnglish ? "Open Beta Center" : "Beta-Zentrale öffnen",
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Style = (Style)Application.Current.Resources["AccentButtonStyle"]
                };
                betaHubButton.Click += async (_, __) => await ShowBetaHubAsync();
                updateContent.Children.Add(betaHubButton);

                var betaFeedbackButton = new Button
                {
                    Content = updateUiEnglish ? "Report a beta problem" : "Beta-Problem melden",
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                betaFeedbackButton.Click += async (_, __) => await OpenBetaFeedbackAsync(settingsWindow);
                updateContent.Children.Add(betaFeedbackButton);
            }

            if (IsBetaBuild)
            {
                var returnToStableButton = new Button
                {
                    Content = updateUiEnglish
                        ? "Return to the stable version"
                        : "Zur stabilen Version zurückkehren",
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                returnToStableButton.Click += async (_, __) =>
                {
                    returnToStableButton.IsEnabled = false;
                    updateButton.IsEnabled = false;
                    updateStatusText.Text = updateUiEnglish
                        ? "Finding the latest stable version..."
                        : "Suche die neueste stabile Version...";

                    string previousChannel = _settings.UpdateChannel;
                    try
                    {
                        var stableRelease = await UpdateService.GetLatestStableReleaseAsync(
                            _startupCancellation.Token);
                        bool confirmed = await ConfirmAsync(
                            updateUiEnglish ? "Return to Stable?" : "Zur stabilen Version zurückkehren?",
                            updateUiEnglish
                                ? $"Current beta: {CurrentVersion}\nAvailable stable version: {stableRelease.Version}\n\nBeta-only settings may be ignored or reset by the stable version. WinVora creates an automatic settings backup before continuing."
                                : $"Aktuelle Beta: {CurrentVersion}\nVerfügbare Stable-Version: {stableRelease.Version}\n\nReine Beta-Einstellungen können von der stabilen Version ignoriert oder zurückgesetzt werden. WinVora erstellt vorher automatisch eine Einstellungssicherung.",
                            primaryButtonText: updateUiEnglish ? "Install Stable" : "Stable installieren",
                            respectDeleteConfirmationSetting: false);
                        if (!confirmed)
                        {
                            updateStatusText.Text = updateUiEnglish
                                ? $"Current version: {CurrentVersion}"
                                : $"Aktuelle Version: {CurrentVersion}";
                            returnToStableButton.IsEnabled = true;
                            updateButton.IsEnabled = true;
                            return;
                        }

                        SettingsBackupService.CreateAutomatic(_settings, "before-stable-return");
                        updateProgressBar.Visibility = Visibility.Visible;
                        updateProgressBar.Value = 0;
                        updateStatusText.Text = updateUiEnglish
                            ? $"Downloading stable version {stableRelease.Version}..."
                            : $"Lade stabile Version {stableRelease.Version} herunter...";

                        var progress = new Progress<DownloadProgressInfo>(info =>
                        {
                            if (info.TotalBytes > 0)
                            {
                                updateProgressBar.IsIndeterminate = false;
                                updateProgressBar.Value = (double)info.BytesReceived / info.TotalBytes * 100;
                            }
                            else
                            {
                                updateProgressBar.IsIndeterminate = true;
                            }
                        });

                        var installerPath = await UpdateService.DownloadUpdateAsync(
                            stableRelease,
                            progress,
                            _startupCancellation.Token);
                        _settings.UpdateChannel = "Stable";
                        _settings.Save();
                        Logger.Log($"Rückkehr zur stabilen Version {stableRelease.Version}: Installer wird gestartet.");
                        UpdateService.RunInstaller(installerPath);
                        Application.Current.Exit();
                    }
                    catch (Exception ex)
                    {
                        _settings.UpdateChannel = previousChannel;
                        _settings.Save();
                        Logger.LogError("ReturnToStable", ex);
                        updateStatusText.Text = updateUiEnglish
                            ? "The stable version could not be installed. Please try again later."
                            : "Die stabile Version konnte nicht installiert werden. Bitte versuche es später erneut.";
                        updateProgressBar.IsIndeterminate = false;
                        updateProgressBar.Visibility = Visibility.Collapsed;
                        returnToStableButton.IsEnabled = true;
                        updateButton.IsEnabled = true;
                    }
                };
                updateContent.Children.Add(returnToStableButton);
            }

            panel.Children.Add(updateCard);

            // ---- Darstellung ----
            var card = MakeSettingsCard(Localization.T("Settings.Appearance"), out var cardContent);

            // Farbschema: dem Windows-Modus folgen oder fest hell/dunkel.
            var colorSchemeCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            PreventClosedComboBoxWheelChange(colorSchemeCombo);
            var colorSchemeOptions = new (string Value, string De, string En)[]
            {
                ("System", "System", "System"),
                ("Dark", "Dunkel", "Dark"),
                ("Light", "Hell", "Light")
            };
            foreach (var option in colorSchemeOptions)
            {
                var preview = new Ellipse
                {
                    Width = 12,
                    Height = 12,
                    Stroke = (SolidColorBrush)RootGrid.Resources["AppOverlay30"],
                    StrokeThickness = 1,
                    Fill = new SolidColorBrush(option.Value switch
                    {
                        "Dark" => Microsoft.UI.Colors.Black,
                        "Light" => Microsoft.UI.Colors.White,
                        _ => Windows.UI.Color.FromArgb(0xFF, 0x6C, 0x5C, 0xE7)
                    })
                };
                var optionContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9 };
                optionContent.Children.Add(preview);
                optionContent.Children.Add(new TextBlock
                {
                    Text = Localization.CurrentLanguage == "en" ? option.En : option.De
                });
                colorSchemeCombo.Items.Add(new ComboBoxItem { Content = optionContent, Tag = option.Value });
            }
            colorSchemeCombo.SelectedIndex = Math.Max(0,
                Array.FindIndex(colorSchemeOptions, option => option.Value == _settings.ColorScheme));
            colorSchemeCombo.SelectionChanged += (_, __) =>
            {
                if (colorSchemeCombo.SelectedItem is ComboBoxItem item && item.Tag is string scheme)
                {
                    _settings.ColorScheme = scheme;
                    ApplyConfiguredColorScheme();
                    root.RequestedTheme = _isDarkTheme ? ElementTheme.Dark : ElementTheme.Light;
                }
            };
            cardContent.Children.Add(MakeLabeledControl(Localization.T("Settings.ColorScheme"), colorSchemeCombo));

            // Mica-Hintergrund
            var micaToggle = new ToggleSwitch
            {
                Header = Localization.T("Settings.UseMica"),
                IsOn = _settings.UseMica,
                OnContent = Localization.T("Settings.On"),
                OffContent = Localization.T("Settings.Off")
            };
            micaToggle.Toggled += (_, __) =>
            {
                _settings.UseMica = micaToggle.IsOn;
                _settings.Save();

                if (_settings.UseMica && Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported())
                    this.SystemBackdrop = new MicaBackdrop { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.BaseAlt };
                else
                    this.SystemBackdrop = null;
            };
            cardContent.Children.Add(micaToggle);

            // Animationsumfang statt eines missverständlichen Ein/Aus-Schalters.
            var animationCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            PreventClosedComboBoxWheelChange(animationCombo);
            var animationOptions = new (string Value, string De, string En)[]
            {
                ("Full", "Voll", "Full"),
                ("Reduced", "Reduziert", "Reduced"),
                ("Off", "Aus", "Off")
            };
            foreach (var option in animationOptions)
                animationCombo.Items.Add(new ComboBoxItem
                {
                    Content = Localization.CurrentLanguage == "en" ? option.En : option.De,
                    Tag = option.Value
                });
            animationCombo.SelectedIndex = Math.Max(0, Array.FindIndex(animationOptions, option => option.Value == _settings.AnimationMode));
            animationCombo.SelectionChanged += (_, __) =>
            {
                if (animationCombo.SelectedItem is ComboBoxItem item && item.Tag is string mode)
                {
                    _settings.AnimationMode = mode;
                    _settings.Save();
                }
            };
            cardContent.Children.Add(MakeLabeledControl(Localization.T("Settings.Animations"), animationCombo));

            panel.Children.Add(card);

            // ---- Verhalten ----
            var behaviorCard = MakeSettingsCard(Localization.T("Settings.Behavior"), out var behaviorContent);

            // Startseite
            var startupCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            PreventClosedComboBoxWheelChange(startupCombo);
            var startupOptions = new (string Value, string Label)[]
            {
                ("Übersicht", "Dashboard"),
                ("System", "Systeminfo"),
                ("Updates", Localization.CurrentLanguage == "en" ? "Program Updates" : "Programm-Updates"),
                ("Storage", "Dateien"),
            };
            foreach (var opt in startupOptions)
                startupCombo.Items.Add(new ComboBoxItem { Content = opt.Label, Tag = opt.Value });

            startupCombo.SelectedIndex = Array.FindIndex(startupOptions, o => o.Value == _settings.StartupPage);
            if (startupCombo.SelectedIndex < 0) startupCombo.SelectedIndex = 0;

            startupCombo.SelectionChanged += (_, __) =>
            {
                if (startupCombo.SelectedItem is ComboBoxItem item && item.Tag is string value)
                {
                    _settings.StartupPage = value;
                    _settings.Save();
                }
            };
            behaviorContent.Children.Add(MakeLabeledControl(Localization.T("Settings.StartupPage"), startupCombo));

            // Sprache
            var languageCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            PreventClosedComboBoxWheelChange(languageCombo);
            var languageOptions = new (string Value, string Label)[]
            {
                ("de", "Deutsch"),
                ("en", "English"),
            };
            foreach (var opt in languageOptions)
                languageCombo.Items.Add(new ComboBoxItem { Content = opt.Label, Tag = opt.Value });

            languageCombo.SelectedIndex = Array.FindIndex(languageOptions, o => o.Value == _settings.Language);
            if (languageCombo.SelectedIndex < 0) languageCombo.SelectedIndex = 0;

            languageCombo.SelectionChanged += (_, __) =>
            {
                if (languageCombo.SelectedItem is ComboBoxItem item && item.Tag is string value)
                {
                    _settings.Language = value;
                    _settings.Save();
                    Localization.CurrentLanguage = value;
                    ApplyLanguage();
                    RefreshLoadedPagesForLanguageChange();
                }
            };
            behaviorContent.Children.Add(MakeLabeledControl(Localization.T("Settings.LanguageLabel"), languageCombo));

            // Live-Update-Intervall
            var intervalCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            PreventClosedComboBoxWheelChange(intervalCombo);
            var intervalOptions = new[] { 1, 2, 5 };
            foreach (var s in intervalOptions)
                intervalCombo.Items.Add(new ComboBoxItem { Content = $"{s} Sekunde{(s == 1 ? "" : "n")}", Tag = s });

            intervalCombo.SelectedIndex = Array.IndexOf(intervalOptions, _settings.LiveUpdateIntervalSeconds);
            if (intervalCombo.SelectedIndex < 0) intervalCombo.SelectedIndex = 1;

            intervalCombo.SelectionChanged += (_, __) =>
            {
                if (intervalCombo.SelectedItem is ComboBoxItem item && item.Tag is int seconds)
                {
                    _settings.LiveUpdateIntervalSeconds = seconds;
                    _settings.Save();
                    StartLiveUsageTimer();
                }
            };
            behaviorContent.Children.Add(MakeLabeledControl(Localization.T("Settings.UpdateInterval"), intervalCombo));

            // Autostart mit Windows
            var autoStartToggle = new ToggleSwitch
            {
                Header = Localization.T("Settings.AutoStart"),
                IsOn = _settings.AutoStartWithWindows,
                OnContent = Localization.T("Settings.On"),
                OffContent = Localization.T("Settings.Off")
            };
            autoStartToggle.Toggled += (_, __) =>
            {
                _settings.AutoStartWithWindows = autoStartToggle.IsOn;
                _settings.Save();
                ApplyAutoStart(_settings.AutoStartWithWindows);
            };
            behaviorContent.Children.Add(autoStartToggle);

            // Bestätigungsdialoge beim Löschen
            var confirmToggle = new ToggleSwitch
            {
                Header = Localization.T("Settings.DeleteConfirm"),
                IsOn = _settings.ShowDeleteConfirmations,
                OnContent = Localization.T("Settings.On"),
                OffContent = Localization.T("Settings.Off")
            };
            confirmToggle.Toggled += (_, __) =>
            {
                _settings.ShowDeleteConfirmations = confirmToggle.IsOn;
                _settings.Save();
            };
            behaviorContent.Children.Add(confirmToggle);

            var completionNotificationToggle = new ToggleSwitch
            {
                Header = en ? "Notification after updates" : "Benachrichtigung nach Updates",
                IsOn = _settings.NotifyUpdateCompletion,
                OnContent = Localization.T("Settings.On"),
                OffContent = Localization.T("Settings.Off")
            };
            completionNotificationToggle.Toggled += (_, __) =>
            {
                _settings.NotifyUpdateCompletion = completionNotificationToggle.IsOn;
                _settings.Save();
            };
            behaviorContent.Children.Add(completionNotificationToggle);

            var restartNotificationToggle = new ToggleSwitch
            {
                Header = en ? "Restart notifications" : "Neustart-Benachrichtigungen",
                IsOn = _settings.NotifyRestartRequired,
                OnContent = Localization.T("Settings.On"),
                OffContent = Localization.T("Settings.Off")
            };
            restartNotificationToggle.Toggled += (_, __) =>
            {
                _settings.NotifyRestartRequired = restartNotificationToggle.IsOn;
                _settings.Save();
            };
            behaviorContent.Children.Add(restartNotificationToggle);

            var securityCard = MakeSettingsCard(en ? "Security" : "Sicherheit", out var securityContent);
            foreach (var protection in new[]
            {
                (Header: en ? "Protect Downloads" : "Downloads besonders schützen", Get: (Func<bool>)(() => _settings.ConfirmDownloadsCleanup), Set: (Action<bool>)(value => _settings.ConfirmDownloadsCleanup = value)),
                (Header: en ? "Protect Recycle Bin" : "Papierkorb besonders schützen", Get: (Func<bool>)(() => _settings.ConfirmRecycleBinCleanup), Set: (Action<bool>)(value => _settings.ConfirmRecycleBinCleanup = value)),
                (Header: en ? "Protect browser data" : "Browserdaten besonders schützen", Get: (Func<bool>)(() => _settings.ConfirmBrowserCleanup), Set: (Action<bool>)(value => _settings.ConfirmBrowserCleanup = value)),
                (Header: en ? "Offer leftover scan after uninstall" : "Nach Deinstallation nach Resten suchen", Get: (Func<bool>)(() => _settings.OfferUninstallLeftoverScan), Set: (Action<bool>)(value => _settings.OfferUninstallLeftoverScan = value))
            })
            {
                var toggle = new ToggleSwitch
                {
                    Header = protection.Header,
                    IsOn = protection.Get(),
                    OnContent = Localization.T("Settings.On"),
                    OffContent = Localization.T("Settings.Off")
                };
                toggle.Toggled += (_, __) =>
                {
                    protection.Set(toggle.IsOn);
                    _settings.Save();
                };
                securityContent.Children.Add(toggle);
            }

            var growthThreshold = new ComboBox { Header = en ? "Folder growth warning" : "Warnung bei Ordnerwachstum", HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var option in new[] { 500L * 1024 * 1024, 1024L * 1024 * 1024, 5L * 1024 * 1024 * 1024 })
                growthThreshold.Items.Add(new ComboBoxItem { Content = StorageService.FormatBytes(option), Tag = option });
            growthThreshold.SelectedIndex = _settings.StorageGrowthWarningBytes >= 5L * 1024 * 1024 * 1024 ? 2 : _settings.StorageGrowthWarningBytes >= 1024L * 1024 * 1024 ? 1 : 0;
            growthThreshold.SelectionChanged += (_, __) =>
            {
                if (growthThreshold.SelectedItem is ComboBoxItem item && item.Tag is long bytes)
                { _settings.StorageGrowthWarningBytes = bytes; _settings.Save(); }
            };
            PreventClosedComboBoxWheelChange(growthThreshold);
            securityContent.Children.Add(growthThreshold);

            panel.Children.Add(behaviorCard);
            panel.Children.Add(securityCard);

            // ---- Dauerhaft ignorierte Updates ----
            var ignoredUpdatesCard = MakeSettingsCard(
                en ? "Permanently ignored updates" : "Dauerhaft ignorierte Updates",
                out var ignoredUpdatesContent);
            if (_settings.IgnoredUpdateIds.Count == 0)
            {
                ignoredUpdatesContent.Children.Add(new TextBlock
                {
                    Text = en ? "No programs are permanently ignored." : "Es werden keine Programme dauerhaft ignoriert.",
                    Foreground = (SolidColorBrush)RootGrid.Resources["AppMutedForegroundBrush"]
                });
            }
            else
            {
                foreach (string ignoredId in _settings.IgnoredUpdateIds.ToList())
                {
                    var ignoredPackage = _cachedPackages?.FirstOrDefault(package =>
                        package.Id.Equals(ignoredId, StringComparison.OrdinalIgnoreCase));
                    var row = new Grid { ColumnSpacing = 10 };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.Children.Add(new TextBlock
                    {
                        Text = ignoredPackage == null
                            ? ignoredId
                            : $"{ignoredPackage.Name}\n{ignoredId} · {ignoredPackage.Version} → {ignoredPackage.Available}",
                        VerticalAlignment = VerticalAlignment.Center,
                        TextWrapping = TextWrapping.Wrap
                    });
                    var restore = new Button { Content = en ? "Allow updates" : "Updates wieder erlauben" };
                    restore.Click += (_, __) =>
                    {
                        _settings.IgnoredUpdateIds.RemoveAll(id => id.Equals(ignoredId, StringComparison.OrdinalIgnoreCase));
                        _settings.Save();
                        row.Visibility = Visibility.Collapsed;
                        _cachedPackages = null;
                    };
                    Grid.SetColumn(restore, 1); row.Children.Add(restore);
                    ignoredUpdatesContent.Children.Add(row);
                }
            }
            panel.Children.Add(ignoredUpdatesCard);

            // ---- Wartung ----
            var maintenanceCard = MakeSettingsCard(Localization.T("Settings.Maintenance"), out var maintenanceContent);
            maintenanceContent.Spacing = 14;

            var logButtonsPanel = new StackPanel { Spacing = 8 };
            maintenanceContent.Children.Add(new TextBlock
            {
                Text = en ? "Logs" : "Protokolle",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (SolidColorBrush)RootGrid.Resources["AppMutedForegroundBrush"]
            });

            var openLogButton = new Button { Content = Localization.T("Settings.OpenLog") };
            openLogButton.Click += (_, __) =>
            {
                try
                {
                    var path = Logger.GetLogFilePath();
                    if (File.Exists(path))
                    {
                        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError("OpenLogButton", ex);
                }
            };
            logButtonsPanel.Children.Add(openLogButton);

            var clearLogButton = new Button { Content = Localization.T("Settings.ClearLog") };
            clearLogButton.Click += (_, __) =>
            {
                Logger.Clear();
            };
            logButtonsPanel.Children.Add(clearLogButton);

            maintenanceContent.Children.Add(logButtonsPanel);

            string backupDirectory = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(AppSettings.GetSettingsFilePath())!, "Backups");
            if (Directory.Exists(backupDirectory))
            {
                var backups = Directory.GetFiles(backupDirectory, "settings-*.json")
                    .OrderByDescending(File.GetLastWriteTimeUtc).Take(1).ToList();
                if (backups.Count > 0)
                {
                    maintenanceContent.Children.Add(new TextBlock
                    {
                        Text = en ? "Latest settings backup" : "Letzte Einstellungssicherung",
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                    });
                    foreach (string backup in backups)
                    {
                        var restoreBackup = new Button
                        {
                            Content = $"{(en ? "Restore" : "Wiederherstellen")} · {File.GetLastWriteTime(backup):g}",
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            Tag = backup
                        };
                        restoreBackup.Click += async (_, __) => await RestoreSettingsBackupAsync(backup);
                        maintenanceContent.Children.Add(restoreBackup);
                    }
                }
            }

            var diagnosticButton = new Button
            {
                Content = en ? "Create anonymized support report" : "Anonymisierten Supportbericht erstellen",
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            maintenanceContent.Children.Add(new TextBlock
            {
                Text = en ? "Support" : "Support",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (SolidColorBrush)RootGrid.Resources["AppMutedForegroundBrush"]
            });
            diagnosticButton.Click += async (_, __) => await ExportDiagnosticReportAsync(settingsWindow);
            maintenanceContent.Children.Add(diagnosticButton);

            var resetButton = new Button
            {
                Content = Localization.T("Settings.ResetSettings"),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = (SolidColorBrush)RootGrid.Resources["AppDangerSurfaceBrush"],
                Foreground = (SolidColorBrush)RootGrid.Resources["AppErrorBrush"],
                BorderThickness = new Thickness(0)
            };
            resetButton.Click += async (_, __) =>
            {
                var confirmed = await ConfirmResetAsync();
                if (!confirmed) return;

                ApplyAutoStart(false);

                _settings = new AppSettings();
                _settings.Save();

                ApplyConfiguredColorScheme();
                if (_settings.UseMica && Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported())
                    this.SystemBackdrop = new MicaBackdrop { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.BaseAlt };
                StartLiveUsageTimer();

                _settingsWindow?.Close();
                await Task.Delay(120);
                DispatcherQueue.TryEnqueue(() => SettingsButton_Click(this, new RoutedEventArgs()));
            };
            maintenanceContent.Children.Add(resetButton);

            void AddSectionReset(StackPanel target, string sectionName, Action reset)
            {
                var sectionReset = new Button
                {
                    Content = en ? "Restore section defaults" : "Bereich zurücksetzen",
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                sectionReset.Click += async (_, __) =>
                {
                    var confirmation = CommonUiBuilder.CreateConfirmation(
                        root.XamlRoot,
                        en ? $"Reset {sectionName}?" : $"{sectionName} zurücksetzen?",
                        en ? "Only the settings in this section will be restored." : "Nur die Einstellungen dieses Bereichs werden auf Standard gesetzt.",
                        en ? "Reset" : "Zurücksetzen",
                        en ? "Cancel" : "Abbrechen");
                    if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;
                    reset();
                    _settings.Validate();
                    _settings.Save();
                    ApplyConfiguredColorScheme(false);
                    StartLiveUsageTimer();
                    settingsWindow.Close();
                    ShowInfo(en ? $"{sectionName} was reset." : $"{sectionName} wurde zurückgesetzt.", InfoBarSeverity.Success);
                    await Task.Delay(120);
                    DispatcherQueue.TryEnqueue(() => SettingsButton_Click(this, new RoutedEventArgs()));
                };
                target.Children.Add(sectionReset);
            }

            AddSectionReset(updateContent, en ? "Updates" : "Updates", () =>
            {
                _settings.NotifyUpdateCompletion = true;
                _settings.NotifyRestartRequired = true;
                _settings.UpdateChannel = "Stable";
                _settings.IgnoredUpdateIds.Clear();
                _settings.ElevatedUpdateIds.Clear();
                _settings.ShutdownUpdateIds.Clear();
            });
            AddSectionReset(cardContent, en ? "Appearance" : "Darstellung", () =>
            {
                _settings.ColorScheme = "System";
                _settings.UseMica = true;
                _settings.GlassIntensity = 18;
                _settings.AnimationMode = "Full";
            });
            AddSectionReset(behaviorContent, en ? "Behavior" : "Verhalten", () =>
            {
                ApplyAutoStart(false);
                _settings.AutoStartWithWindows = false;
                _settings.StartupPage = "Übersicht";
                _settings.LiveUpdateIntervalSeconds = 2;
            });
            AddSectionReset(securityContent, en ? "Security" : "Sicherheit", () =>
            {
                _settings.ShowDeleteConfirmations = true;
                _settings.ConfirmDownloadsCleanup = true;
                _settings.ConfirmRecycleBinCleanup = true;
                _settings.ConfirmBrowserCleanup = true;
                _settings.OfferUninstallLeftoverScan = true;
                _settings.StorageGrowthWarningBytes = 1024L * 1024 * 1024;
            });
            AddSectionReset(maintenanceContent, en ? "Maintenance" : "Wartung", () =>
            {
                _settings.SettingsWindowWidth = 560;
                _settings.SettingsWindowHeight = 680;
                _settings.ChangelogWindowWidth = 560;
                _settings.ChangelogWindowHeight = 720;
            });

            panel.Children.Add(maintenanceCard);

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(0, 0, 22, 0),
                Content = panel
            };
            scrollViewer.Resources["ScrollBarSize"] = 16d;
            scrollViewer.Resources["ScrollBarVerticalThumbMinWidth"] = 10d;

            var categoryNavigation = new StackPanel
            {
                Spacing = 6,
                Width = 156,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 18, 0)
            };
            var categoryButtons = new List<Button>();
            void SetActiveCategory(Button activeButton)
            {
                foreach (var button in categoryButtons)
                {
                    bool active = ReferenceEquals(button, activeButton);
                    button.Background = active
                        ? (SolidColorBrush)RootGrid.Resources["AppAccentOverlay20"]
                        : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                    button.BorderThickness = new Thickness(active ? 3 : 0, 0, 0, 0);
                    button.BorderBrush = active
                        ? (SolidColorBrush)RootGrid.Resources["AppAccentBrushLight"]
                        : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                }
            }
            foreach (var category in new[]
            {
                (Label: en ? "Updates" : "Updates", Target: updateCard, Glyph: "\uE895"),
                (Label: en ? "Appearance" : "Darstellung", Target: card, Glyph: "\uE790"),
                (Label: en ? "Behavior" : "Verhalten", Target: behaviorCard, Glyph: "\uE713"),
                (Label: en ? "Security" : "Sicherheit", Target: securityCard, Glyph: "\uEA18"),
                (Label: en ? "Maintenance" : "Wartung", Target: maintenanceCard, Glyph: "\uE74D")
            })
            {
                var navigationButton = new Button
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(10, 9, 10, 9),
                    MinHeight = 40,
                    Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 9,
                        Children =
                        {
                            new FontIcon { Glyph = category.Glyph, FontSize = 14 },
                            new TextBlock { Text = category.Label, FontSize = 13 }
                        }
                    }
                };
                categoryButtons.Add(navigationButton);
                navigationButton.Click += (_, __) =>
                {
                    SetActiveCategory(navigationButton);
                    category.Target.StartBringIntoView(new BringIntoViewOptions
                    {
                        AnimationDesired = _settings.AnimationMode == "Full",
                        VerticalAlignmentRatio = 0
                    });
                };
                categoryNavigation.Children.Add(navigationButton);
            }
            if (categoryButtons.Count > 0)
                SetActiveCategory(categoryButtons[0]);

            var navigationCard = new Border
            {
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 18, 0),
                Background = (SolidColorBrush)RootGrid.Resources["AppOverlay10"],
                Child = categoryNavigation
            };
            categoryNavigation.Children.Add(new Border
            {
                Height = 1,
                Margin = new Thickness(4, 8, 4, 4),
                Background = (SolidColorBrush)RootGrid.Resources["AppOverlay22"]
            });
            categoryNavigation.Children.Add(new TextBlock
            {
                Text = en ? "Changes are saved automatically." : "Änderungen werden automatisch gespeichert.",
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(8, 4, 8, 4),
                Foreground = (SolidColorBrush)RootGrid.Resources["AppFaintForegroundBrush"]
            });
            categoryNavigation.Margin = new Thickness(0);

            var contentHost = new Grid { Padding = new Thickness(22, 18, 18, 28), ColumnSpacing = 0 };
            contentHost.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            contentHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            contentHost.Children.Add(navigationCard);
            Grid.SetColumn(scrollViewer, 1);
            contentHost.Children.Add(scrollViewer);
            Grid.SetRow(contentHost, 1);

            var divider = MakeTitleBarDivider();
            Grid.SetRow(divider, 0);

            var titleLabel = MakeTitleBarLabel(Localization.T("Settings.WindowTitle"));
            Grid.SetRow(titleLabel, 0);

            root.Children.Add(contentHost);
            root.Children.Add(divider);
            root.Children.Add(titleLabel);

            settingsWindow.Content = root;
            double rasterScale = RootGrid.XamlRoot?.RasterizationScale ?? 1d;
            int settingsWidth = Math.Max(_settings.SettingsWindowWidth, (int)Math.Ceiling(760 * rasterScale));
            int settingsHeight = Math.Max(_settings.SettingsWindowHeight, (int)Math.Ceiling(720 * rasterScale));
            StyleDarkWindow(settingsWindow, settingsWidth, settingsHeight);
            WindowActivationService.PlaceWindow(this, settingsWindow,
                _settings.SettingsWindowX, _settings.SettingsWindowY,
                settingsWidth, settingsHeight);
            settingsWindow.Activate();
            WindowActivationService.ShowOwnedInFront(this, settingsWindow);
        }


        private async Task ExportDiagnosticReportAsync(Window owner)
        {
            SystemInfoSnapshot snapshot;
            try
            {
                snapshot = _cachedSnapshot ?? await SystemInfoProvider.GetFullSnapshotAsync(_startupCancellation.Token);
            }
            catch (Exception ex)
            {
                Logger.LogError("Systeminformationen für Supportbericht", ex);
                snapshot = new SystemInfoSnapshot
                {
                    WindowsEdition = Environment.OSVersion.Platform.ToString(),
                    WindowsVersion = Environment.OSVersion.VersionString,
                    Architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString()
                };
            }
            string log = await Task.Run(() => Logger.ReadForDiagnostics());
            var report = DiagnosticReportBuilder.Build(snapshot, CurrentVersion, log);
            bool en = Localization.CurrentLanguage == "en";
            var previewDialog = new ContentDialog
            {
                XamlRoot = (owner.Content as FrameworkElement)?.XamlRoot ?? RootGrid.XamlRoot,
                Title = en ? "Preview anonymized support report" : "Vorschau des anonymisierten Supportberichts",
                Content = new TextBox
                {
                    Text = report,
                    IsReadOnly = true,
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.NoWrap,
                    MinWidth = 520,
                    MaxHeight = 420
                },
                PrimaryButtonText = en ? "Save" : "Speichern",
                CloseButtonText = en ? "Cancel" : "Abbrechen",
                DefaultButton = ContentDialogButton.Close
            };
            if (await previewDialog.ShowAsync() != ContentDialogResult.Primary) return;
            bool saved = await ReportExportService.SaveSupportZipAsync(owner, $"WinVora-Supportbericht-{CurrentVersion}", report);
            if (saved)
                ShowInfo(Localization.CurrentLanguage == "en" ? "Support report exported." : "Supportbericht wurde exportiert.", InfoBarSeverity.Success);
        }

        private async Task RestoreSettingsBackupAsync(string backupPath)
        {
            bool en = Localization.CurrentLanguage == "en";
            if (!await ConfirmAsync(
                en ? "Restore settings backup?" : "Einstellungssicherung wiederherstellen?",
                $"{File.GetLastWriteTime(backupPath):G}",
                en ? "Restore" : "Wiederherstellen",
                respectDeleteConfirmationSetting: false)) return;
            try
            {
                var restored = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(await File.ReadAllTextAsync(backupPath))
                    ?? throw new InvalidDataException("Ungültige Sicherung.");
                restored.Validate();
                _settings = restored;
                _settings.Save();
                ApplyConfiguredColorScheme();
                ApplyLanguage();
                StartLiveUsageTimer();
                ShowInfo(en ? "Settings backup restored." : "Einstellungssicherung wurde wiederhergestellt.", InfoBarSeverity.Success);
                _settingsWindow?.Close();
            }
            catch (Exception ex)
            {
                Logger.LogError("Einstellungssicherung wiederherstellen", ex);
                ShowInfo(ex.Message, InfoBarSeverity.Error);
            }
        }

    }
}
