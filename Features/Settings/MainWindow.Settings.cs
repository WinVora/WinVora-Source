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
            comboBox.PointerWheelChanged += (_, args) =>
            {
                if (!comboBox.IsDropDownOpen)
                    args.Handled = true;
            };
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

            var titleBar = window.AppWindow.TitleBar;
            var fg = _isDarkTheme ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
            byte rgb = _isDarkTheme ? (byte)0xFF : (byte)0x00;

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

            // BUGFIX: Die Drag-Region deckte vorher die komplette Fensterbreite
            // ab - inklusive des Bereichs, in dem Windows die Schließen-/
            // Minimieren-/Maximieren-Buttons zeichnet (rechts, Breite steht in
            // "RightInset"). Das verdrängte/verdeckte die Buttons teilweise.
            // Jetzt bleibt dieser Bereich bewusst ausgespart.
            var rightInset = titleBar.RightInset > 0 ? titleBar.RightInset : 140;
            var dragWidth = Math.Max(width - rightInset, 0);
            titleBar.SetDragRectangles(new[] { new Windows.Graphics.RectInt32(0, 0, dragWidth, 40) });
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
                Background = (SolidColorBrush)RootGrid.Resources["AppRootBackgroundBrush"]
            };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
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
                BorderBrush = (SolidColorBrush)RootGrid.Resources["AppOverlay30"],
                BorderThickness = new Thickness(1)
            };
            settingsSearchBox.Resources["TextControlBorderBrushFocused"] = RootGrid.Resources["AppAccentBrushLight"];
            settingsSearchBox.Resources["TextControlBorderBrushPointerOver"] = RootGrid.Resources["AppAccentBrushLight"];
            settingsSearchBox.Resources["TextControlBorderBrush"] = RootGrid.Resources["AppOverlay30"];
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
            settingsSearchBox.TextChanged += (_, __) =>
            {
                string query = settingsSearchBox.Text.Trim();
                int visibleCards = 0;
                foreach (var settingsCard in panel.Children.OfType<Border>().Where(border => border.Tag is string))
                {
                    string searchable = UiTextSearch.Collect(settingsCard);
                    settingsCard.Visibility = string.IsNullOrWhiteSpace(query) ||
                        searchable.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                            ? Visibility.Visible
                            : Visibility.Collapsed;
                    if (settingsCard.Visibility == Visibility.Visible) visibleCards++;
                }
                noSettingsResults.Visibility = visibleCards == 0 ? Visibility.Visible : Visibility.Collapsed;
            };
            panel.Children.Add(settingsSearchBox);
            panel.Children.Add(noSettingsResults);

            // ---- Auto-Update (ganz oben, damit ein verfügbares Update sofort
            //      ins Auge fällt statt unten in der Wartung versteckt zu sein) ----
            var updateCard = MakeSettingsCard(Localization.T("Settings.UpdateSection"), out var updateContent);
            bool updateUiEnglish = Localization.CurrentLanguage == "en";

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
                        update = await UpdateService.CheckForUpdateAsync(CurrentVersion);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError("CheckForUpdateAsync", ex);
                        updateStatusText.Text = ex switch
                        {
                            HttpRequestException => updateUiEnglish
                                ? "GitHub could not be reached. Please check your internet connection."
                                : "GitHub ist nicht erreichbar. Bitte prüfe deine Internetverbindung.",
                            InvalidDataException => updateUiEnglish
                                ? "The new version has no installer available yet."
                                : "Für die neue Version ist noch kein Installer verfügbar.",
                            _ => updateUiEnglish
                                ? "The update check failed. Please try again later."
                                : "Die Update-Prüfung ist fehlgeschlagen. Bitte versuche es später erneut."
                        };
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
                    respectDeleteConfirmationSetting: false);

                if (!confirmed)
                {
                    updateStatusText.Text = updateUiEnglish
                        ? $"Update {update.Version} is available but was not installed."
                        : $"Update auf {update.Version} verfügbar, aber nicht installiert.";
                    updateButton.IsEnabled = true;
                    return;
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
                    var installerPath = await UpdateService.DownloadUpdateAsync(update, progress);
                    Logger.Log($"Update auf Version {update.Version} heruntergeladen, starte Installer.");

                    UpdateService.RunInstaller(installerPath);

                    // App schließt sich selbst, damit der Installer die Dateien
                    // ungehindert überschreiben kann.
                    Application.Current.Exit();
                }
                catch (Exception ex)
                {
                    Logger.LogError("DownloadUpdateAsync/RunInstaller", ex);
                    updateStatusText.Text = ex is InvalidDataException
                        ? (updateUiEnglish
                            ? "The download is damaged or incomplete and was removed."
                            : "Der Download ist beschädigt oder unvollständig und wurde entfernt.")
                        : (updateUiEnglish
                            ? "The update could not be installed. Please try again later."
                            : "Das Update konnte nicht installiert werden. Bitte versuche es später erneut.");
                    updateProgressBar.Visibility = Visibility.Collapsed;
                    updateButton.IsEnabled = true;
                }
            };
            updateContent.Children.Add(updateButton);

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
                    _settings.ReducedMotion = mode != "Full";
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
                behaviorContent.Children.Add(toggle);
            }

            panel.Children.Add(behaviorCard);

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

            var logButtonsPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };

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

            var settingsTransferPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            var exportSettingsButton = new Button { Content = en ? "Export settings" : "Einstellungen exportieren" };
            exportSettingsButton.Click += async (_, __) => await ExportSettingsAsync(settingsWindow);
            var importSettingsButton = new Button { Content = en ? "Import settings" : "Einstellungen importieren" };
            importSettingsButton.Click += async (_, __) => await ImportSettingsAsync(settingsWindow);
            settingsTransferPanel.Children.Add(exportSettingsButton);
            settingsTransferPanel.Children.Add(importSettingsButton);
            maintenanceContent.Children.Add(settingsTransferPanel);

            string backupDirectory = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(AppSettings.GetSettingsFilePath())!, "Backups");
            if (Directory.Exists(backupDirectory))
            {
                var backups = Directory.GetFiles(backupDirectory, "settings-*.json")
                    .OrderByDescending(File.GetLastWriteTimeUtc).Take(5).ToList();
                if (backups.Count > 0)
                {
                    maintenanceContent.Children.Add(new TextBlock
                    {
                        Text = en ? "Available settings backups" : "Verfügbare Einstellungssicherungen",
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
            diagnosticButton.Click += async (_, __) => await ExportDiagnosticReportAsync(settingsWindow);
            maintenanceContent.Children.Add(diagnosticButton);

            var resetButton = new Button
            {
                Content = Localization.T("Settings.ResetSettings"),
                HorizontalAlignment = HorizontalAlignment.Stretch
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
            };
            maintenanceContent.Children.Add(resetButton);

            panel.Children.Add(maintenanceCard);

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(0, 0, 14, 0),
                Content = panel
            };

            var contentHost = new Grid { Padding = new Thickness(28, 18, 18, 28) };
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
            int settingsWidth = Math.Max(_settings.SettingsWindowWidth, 560);
            int settingsHeight = Math.Max(_settings.SettingsWindowHeight, 680);
            StyleDarkWindow(settingsWindow, settingsWidth, settingsHeight);
            WindowActivationService.PlaceWindow(this, settingsWindow,
                _settings.SettingsWindowX, _settings.SettingsWindowY,
                settingsWidth, settingsHeight);
            settingsWindow.Activate();
            WindowActivationService.ShowOwnedInFront(this, settingsWindow);
        }


        private async Task ExportSettingsAsync(Window owner)
        {
            string json = System.Text.Json.JsonSerializer.Serialize(
                _settings, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            bool saved = await ReportExportService.SaveJsonAsync(owner, $"WinVora-Einstellungen-{CurrentVersion}", json);
            if (saved)
                ShowInfo(Localization.CurrentLanguage == "en" ? "Settings exported." : "Einstellungen wurden exportiert.", InfoBarSeverity.Success);
        }

        private async Task ImportSettingsAsync(Window owner)
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.FileTypeFilter.Add(".json");
            WinRT.Interop.InitializeWithWindow.Initialize(
                picker, WinRT.Interop.WindowNative.GetWindowHandle(owner));
            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            try
            {
                string json = await File.ReadAllTextAsync(file.Path);
                var imported = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json)
                    ?? throw new InvalidDataException("Die Datei enthält keine gültigen WinVora-Einstellungen.");
                imported.Validate();

                bool en = Localization.CurrentLanguage == "en";
                var preview = new TextBlock
                {
                    Text = (en ? "The following settings will be imported:" : "Folgende Einstellungen werden importiert:") +
                           $"\n\n{(en ? "Language" : "Sprache")}: {imported.Language}" +
                           $"\n{(en ? "Color scheme" : "Farbschema")}: {imported.ColorScheme}" +
                           $"\n{(en ? "Startup page" : "Startseite")}: {imported.StartupPage}" +
                           $"\n{(en ? "Ignored updates" : "Ignorierte Updates")}: {imported.IgnoredUpdateIds.Count}" +
                           $"\n{(en ? "History entries" : "Verlaufseinträge")}: {imported.ActivityLog.Count}" +
                           (en ? "\n\nYour current settings are backed up first." : "\n\nDie aktuellen Einstellungen werden vorher gesichert."),
                    TextWrapping = TextWrapping.Wrap
                };
                var confirmation = new ContentDialog
                {
                    XamlRoot = (owner.Content as FrameworkElement)?.XamlRoot ?? RootGrid.XamlRoot,
                    Title = en ? "Import settings?" : "Einstellungen importieren?",
                    Content = preview,
                    PrimaryButtonText = en ? "Import" : "Importieren",
                    CloseButtonText = en ? "Cancel" : "Abbrechen",
                    DefaultButton = ContentDialogButton.Close
                };
                if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;

                string currentPath = AppSettings.GetSettingsFilePath();
                if (File.Exists(currentPath))
                {
                    string backupDirectory = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(currentPath)!, "Backups");
                    Directory.CreateDirectory(backupDirectory);
                    string backupPath = System.IO.Path.Combine(backupDirectory, $"settings-{DateTime.Now:yyyyMMdd-HHmmss}.json");
                    File.Copy(currentPath, backupPath, overwrite: false);
                }
                _settings = imported;
                _settings.Save();
                ApplyConfiguredColorScheme();
                ApplyLanguage();
                StartLiveUsageTimer();
                ShowInfo(Localization.CurrentLanguage == "en" ? "Settings imported." : "Einstellungen wurden importiert.", InfoBarSeverity.Success);
                _settingsWindow?.Close();
            }
            catch (Exception ex)
            {
                Logger.LogError("Einstellungen importieren", ex);
                ShowInfo((Localization.CurrentLanguage == "en" ? "Import failed: " : "Import fehlgeschlagen: ") + ex.Message, InfoBarSeverity.Error);
            }
        }

        private async Task ExportDiagnosticReportAsync(Window owner)
        {
            var snapshot = _cachedSnapshot ?? await SystemInfoProvider.GetFullSnapshotAsync(_startupCancellation.Token);
            string log = File.Exists(Logger.GetLogFilePath())
                ? await File.ReadAllTextAsync(Logger.GetLogFilePath())
                : "Kein Protokoll vorhanden.";
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
