using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ToolkitControls = CommunityToolkit.WinUI.Controls;

namespace WinVora
{
    public sealed partial class MainWindow : Window
    {
        private int[]? _wingetColumns;
        private bool _initialized;
        private DispatcherTimer? _liveUsageTimer;
        private Window? _changelogWindow;
        private SystemInfoSnapshot? _cachedSnapshot;
        private bool _isLoadingSnapshot;
        private List<WingetPackage>? _cachedPackages;
        private bool _isDarkTheme = true;

        private readonly List<(WingetPackage Package, ToggleSwitch Toggle, ToolkitControls.SettingsCard Card, string BaseDescription)> _wingetRows = new();
        private readonly List<(StorageCategory Category, ToggleSwitch Toggle)> _storageRows = new();
        private AppSettings _settings = AppSettings.Load();


        public MainWindow()
        {
            this.InitializeComponent();
            this.Title = "WinVora";
            this.Activated += MainWindow_Activated;

            // Eigene, dunkle Titelleiste statt der weißen Standard-Leiste von Windows.
            this.ExtendsContentIntoTitleBar = true;
            this.SetTitleBar(TitleBarDragRegion);

            // Das von Windows selbst gezeichnete Icon+Titel-Textfeld neben den
            // Fenster-Buttons folgt dem Windows-Systemthema und lässt sich nicht
            // umfärben - deshalb blenden wir es komplett aus. Unser eigenes
            // "WinVora"-Logo steht ja schon oben in der Sidebar.
            this.AppWindow.TitleBar.IconShowOptions = Microsoft.UI.Windowing.IconShowOptions.HideIconAndSystemMenu;

            // App-Icon für Titelleiste/Taskleiste setzen (liegt neben der .exe im Ausgabeverzeichnis).
            try
            {
                this.AppWindow.SetIcon("app.ico");
            }
            catch { /* Icon nicht kritisch - App startet auch ohne */ }

            // Echtes Mica-Backdrop fürs Fenster (fällt automatisch auf die
            // Acrylic-Hintergründe im XAML zurück, falls Mica nicht unterstützt wird).
            if (_settings.UseMica && Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported())
            {
                this.SystemBackdrop = new MicaBackdrop { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.BaseAlt };
            }

            // Wendet den gespeicherten Hell-/Dunkel-Modus an (Titelleiste, Theme-Brushes,
            // Glas-Intensität, RequestedTheme für Standard-Controls wie Buttons/Toggles).
            ApplyTheme(_settings.DarkMode, persist: false);
        }

        // Zentrale Stelle für den Hell-/Dunkel-Modus-Wechsel. Setzt sowohl unsere
        // eigenen, fest referenzierten Theme-Brushes (siehe Window.Resources) als
        // auch das Fluent-RequestedTheme, damit Standard-Controls (Buttons ohne
        // eigene Foreground-Angabe, ToggleSwitch, Expander, ScrollBar, ProgressRing...)
        // automatisch mit umschalten.
        private void ApplyTheme(bool dark, bool persist = true)
        {
            _isDarkTheme = dark;
            byte rgb = dark ? (byte)0xFF : (byte)0x00;

            void SetOverlay(string key, byte alpha)
            {
                if (RootGrid.Resources.TryGetValue(key, out var value) && value is SolidColorBrush brush)
                    brush.Color = Windows.UI.Color.FromArgb(alpha, rgb, rgb, rgb);
            }

            SetOverlay("AppForegroundBrush", 0xFF);
            SetOverlay("AppMutedForegroundBrush", 0xB0);
            SetOverlay("AppFaintForegroundBrush", 0xAA);
            SetOverlay("AppOverlay10", 0x10);
            SetOverlay("AppOverlay18", 0x18);
            SetOverlay("AppOverlay1A", 0x1A);
            SetOverlay("AppOverlay1E", 0x1E);
            SetOverlay("AppOverlay22", 0x22);
            SetOverlay("AppOverlay26", 0x26);
            SetOverlay("AppOverlay28", 0x28);
            SetOverlay("AppOverlay30", 0x30);
            SetOverlay("AppForegroundC0", 0xC0);
            SetOverlay("AppForegroundCC", 0xCC);
            SetOverlay("AppForegroundD8", 0xD8);

            if (RootGrid.Resources["AppRootBackgroundBrush"] is SolidColorBrush rootBrush)
                rootBrush.Color = dark ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White;

            RootGrid.RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light;

            ApplyTitleBarColors(dark);
            ApplyGlassIntensity(_settings.GlassIntensity);

            if (persist)
            {
                _settings.DarkMode = dark;
                _settings.Save();
            }
        }

        // Ausgelagert aus dem Konstruktor, damit die Titelleisten-Farben beim
        // Umschalten des Hell-/Dunkel-Modus live mit angepasst werden können.
        private void ApplyTitleBarColors(bool dark)
        {
            var titleBar = this.AppWindow.TitleBar;
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
        }

        // Passt die Deckkraft der Glas-Karten (Sidebar/Hauptbereich) live an.
        private void ApplyGlassIntensity(int alpha)
        {
            alpha = Math.Clamp(alpha, 0, 64);
            byte a = (byte)alpha;
            byte borderA = (byte)Math.Min(alpha + 14, 90);
            byte rgb = _isDarkTheme ? (byte)0xFF : (byte)0x00;

            SidebarCard.Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(a, rgb, rgb, rgb));
            SidebarCard.BorderBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(borderA, rgb, rgb, rgb));

            byte mainA = (byte)Math.Max(alpha - 8, 0);
            MainCard.Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(mainA, rgb, rgb, rgb));
            MainCard.BorderBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(borderA, rgb, rgb, rgb));
        }

        // Nur beim allerersten Aktivieren die Startseite laden,
        // nicht bei jedem Fokuswechsel (Alt-Tab etc.).
        private async void MainWindow_Activated(object sender, WindowActivatedEventArgs e)
        {
            if (_initialized) return;
            _initialized = true;

            this.Activated -= MainWindow_Activated;

            await LoadInitialDataAsync();

            HideStartupOverlay();
        }

        // BUGFIX: Der Ladebildschirm wurde vorher sofort wieder ausgeblendet,
        // ohne dass irgendetwas geladen wurde - man landete auf einer leeren
        // Übersicht ("--%"), die sich erst danach sichtbar aufgebaut hat.
        // Jetzt bleibt der Ladebildschirm sichtbar, bis Systeminfos und
        // Winget-Status wirklich fertig geladen sind.
        private async Task LoadInitialDataAsync()
        {
            StartupStatusText.Text = "Systeminfos werden geladen...";

            try
            {
                _cachedSnapshot = await SystemInfoProvider.GetFullSnapshotAsync();
                ApplySnapshot(_cachedSnapshot);
            }
            catch
            {
                // Wird beim Aufruf der Systeminfo-Seite erneut versucht,
                // falls es hier ausnahmsweise fehlschlägt.
            }

            StartupStatusText.Text = "Updates werden geprüft...";

            try
            {
                await LoadWinget();
            }
            catch
            {
                // Wird auf der Winget-Seite mit Fehlermeldung sichtbar,
                // falls es hier fehlschlägt.
            }

            StartLiveUsageTimer();

            // Konfigurierte Startseite anzeigen (Standard: Übersicht).
            switch (_settings.StartupPage)
            {
                case "System":
                    SetPage("System");
                    break;

                case "Updates":
                    SetPage("Updates");
                    if (_cachedPackages != null) RenderWingetPackages(_cachedPackages);
                    break;

                case "Storage":
                    SetPage("Storage");
                    await LoadStorage();
                    break;

                default:
                    SetPage("Übersicht");
                    break;
            }
        }

        private void HideStartupOverlay()
        {
            var animation = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(350)
            };

            var storyboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animation, StartupOverlay);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animation, "Opacity");
            storyboard.Children.Add(animation);

            storyboard.Completed += (_, __) => StartupOverlay.Visibility = Visibility.Collapsed;
            storyboard.Begin();
        }

        private void SetPage(string title)
        {
            PageTitle.Text = title;
            PageSubtitle.Text = "";

            OverviewPanel.Visibility = title == "Übersicht" ? Visibility.Visible : Visibility.Collapsed;
            SystemPanel.Visibility = title == "System" ? Visibility.Visible : Visibility.Collapsed;
            ContentArea.Visibility = title == "Updates" ? Visibility.Visible : Visibility.Collapsed;
            StoragePanel.Visibility = title == "Storage" ? Visibility.Visible : Visibility.Collapsed;

            AppsActionBar.Visibility = title == "Updates" ? Visibility.Visible : Visibility.Collapsed;
            SystemActionBar.Visibility = title == "System" ? Visibility.Visible : Visibility.Collapsed;
            StorageActionBar.Visibility = title == "Storage" ? Visibility.Visible : Visibility.Collapsed;
            UpdateProgressPanel.Visibility = Visibility.Collapsed;
            StorageProgressPanel.Visibility = Visibility.Collapsed;

            ContentArea.Children.Clear();
            StoragePanel.Children.Clear();

            if (title != "System" && title != "Übersicht")
                _liveUsageTimer?.Stop();

            FadeIn(title switch
            {
                "Übersicht" => OverviewPanel,
                "System" => SystemPanel,
                "Updates" => ContentArea,
                "Storage" => StoragePanel,
                _ => null
            });
        }

        // Sanftes Einblenden der jeweils aktiven Seite beim Wechsel.
        private void FadeIn(UIElement? element)
        {
            if (element == null) return;
            if (_settings.ReducedMotion) return;

            element.Opacity = 0;

            var animation = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(220),
                EasingFunction = new Microsoft.UI.Xaml.Media.Animation.QuadraticEase()
            };

            var storyboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animation, element);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animation, "Opacity");
            storyboard.Children.Add(animation);
            storyboard.Begin();
        }

        // ================= DIALOGE =================

        private const string AutoStartRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AutoStartValueName = "WinVora";

        private void ApplyAutoStart(bool enable)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(AutoStartRegistryKey, writable: true);
                if (key == null) return;

                if (enable)
                {
                    var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                    if (!string.IsNullOrEmpty(exePath))
                        key.SetValue(AutoStartValueName, $"\"{exePath}\"");
                }
                else
                {
                    key.DeleteValue(AutoStartValueName, throwOnMissingValue: false);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("ApplyAutoStart", ex);
            }
        }

        private async Task<bool> ConfirmResetAsync()
        {
            var dialog = new ContentDialog
            {
                Title = "Einstellungen zurücksetzen?",
                Content = "Alle Einstellungen werden auf die Standardwerte zurückgesetzt. Fortfahren?",
                PrimaryButtonText = "Zurücksetzen",
                CloseButtonText = "Abbrechen",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        }

        private async Task<bool> ConfirmAsync(string title, string message)
        {
            if (!_settings.ShowDeleteConfirmations) return true;

            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                PrimaryButtonText = "Löschen",
                CloseButtonText = "Abbrechen",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        }

        private Window? _settingsWindow;

        // Baut eine Einstellungs-Karte mit Überschrift und liefert das
        // StackPanel zurück, in das die eigentlichen Controls kommen -
        // vermeidet die Wiederholung von Border/Padding/Farben pro Karte.
        private Border MakeSettingsCard(string title, out StackPanel content)
        {
            var card = new Border
            {
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(20),
                Background = (SolidColorBrush)RootGrid.Resources["AppOverlay18"],
                BorderBrush = (SolidColorBrush)RootGrid.Resources["AppOverlay28"],
                BorderThickness = new Thickness(1)
            };

            content = new StackPanel { Spacing = 20 };
            content.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 18,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (SolidColorBrush)RootGrid.Resources["AppForegroundBrush"]
            });

            card.Child = content;
            return card;
        }

        // Kleines Label+Control-Paar (z.B. für ComboBoxen mit Beschriftung).
        private StackPanel MakeLabeledControl(string label, FrameworkElement control)
        {
            var labelBlock = new TextBlock
            {
                Text = label,
                Foreground = (SolidColorBrush)RootGrid.Resources["AppForegroundBrush"],
                FontSize = 14
            };
            return new StackPanel { Spacing = 6, Children = { labelBlock, control } };
        }

        // Wendet die gleiche dunkle Titelleiste wie beim Hauptfenster auch auf
        // Popup-Fenster (Einstellungen, Changelog) an - sonst zeigen die die
        // weiße Windows-Standardleiste, obwohl der Rest der App dunkel ist.
        private void StyleDarkWindow(Window window, int width, int height)
        {
            window.ExtendsContentIntoTitleBar = true;
            window.AppWindow.Resize(new Windows.Graphics.SizeInt32(width, height));

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

            // Ohne eigenes Drag-Element (wie TitleBarDragRegion im Hauptfenster)
            // muss der Zieh-Bereich hier manuell als Rechteck angegeben werden,
            // sonst lässt sich das Fenster nicht mehr per Maus verschieben.
            titleBar.SetDragRectangles(new[] { new Windows.Graphics.RectInt32(0, 0, width, 40) });
        }

        // Dünne Trennlinie unter der (ausgeblendeten) Titelleiste. Wird in eine
        // eigene, feste Grid.Row (nicht in den scrollbaren Bereich) gesetzt,
        // damit sie garantiert nicht mitscrollt.
        private Border MakeTitleBarDivider() => new()
        {
            Height = 1,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = (SolidColorBrush)RootGrid.Resources["AppOverlay1E"]
        };

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            _settingsWindow = new Window { Title = "WinVora Einstellungen" };

            var root = new Grid
            {
                Background = (SolidColorBrush)RootGrid.Resources["AppRootBackgroundBrush"]
            };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RequestedTheme = _isDarkTheme ? ElementTheme.Dark : ElementTheme.Light;

            var panel = new StackPanel { Spacing = 18, MaxWidth = 420 };

            panel.Children.Add(new TextBlock
            {
                Text = "Einstellungen",
                FontSize = 28,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (SolidColorBrush)RootGrid.Resources["AppForegroundBrush"]
            });

            // ---- Darstellung ----
            var card = MakeSettingsCard("Darstellung", out var cardContent);

            // Heller / Dunkler Modus
            var themeToggle = new ToggleSwitch
            {
                Header = "Heller Modus",
                IsOn = !_settings.DarkMode,
                OnContent = "An",
                OffContent = "Aus"
            };
            themeToggle.Toggled += (_, __) =>
            {
                bool dark = !themeToggle.IsOn;
                ApplyTheme(dark);
                root.RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light;
            };
            cardContent.Children.Add(themeToggle);

            // Mica-Hintergrund
            var micaToggle = new ToggleSwitch
            {
                Header = "Mica-Hintergrund verwenden",
                IsOn = _settings.UseMica,
                OnContent = "An",
                OffContent = "Aus"
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

            // Reduzierte Bewegung
            var motionToggle = new ToggleSwitch
            {
                Header = "Animationen beim Seitenwechsel",
                IsOn = !_settings.ReducedMotion,
                OnContent = "An",
                OffContent = "Aus"
            };
            motionToggle.Toggled += (_, __) =>
            {
                _settings.ReducedMotion = !motionToggle.IsOn;
                _settings.Save();
            };
            cardContent.Children.Add(motionToggle);

            panel.Children.Add(card);

            // ---- Verhalten ----
            var behaviorCard = MakeSettingsCard("Verhalten", out var behaviorContent);

            // Startseite
            var startupCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            var startupOptions = new (string Value, string Label)[]
            {
                ("Übersicht", "Übersicht"),
                ("System", "Systeminfo"),
                ("Updates", "Apps"),
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
            behaviorContent.Children.Add(MakeLabeledControl("Startseite", startupCombo));

            // Live-Update-Intervall
            var intervalCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
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
            behaviorContent.Children.Add(MakeLabeledControl("Aktualisierungsintervall (CPU/RAM)", intervalCombo));

            // Autostart mit Windows
            var autoStartToggle = new ToggleSwitch
            {
                Header = "Mit Windows starten",
                IsOn = _settings.AutoStartWithWindows,
                OnContent = "An",
                OffContent = "Aus"
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
                Header = "Bestätigung vor dem Löschen",
                IsOn = _settings.ShowDeleteConfirmations,
                OnContent = "An",
                OffContent = "Aus"
            };
            confirmToggle.Toggled += (_, __) =>
            {
                _settings.ShowDeleteConfirmations = confirmToggle.IsOn;
                _settings.Save();
            };
            behaviorContent.Children.Add(confirmToggle);

            panel.Children.Add(behaviorCard);

            // ---- Wartung ----
            var maintenanceCard = MakeSettingsCard("Wartung", out var maintenanceContent);
            maintenanceContent.Spacing = 14;

            var logButtonsPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };

            var openLogButton = new Button { Content = "Log-Datei öffnen" };
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

            var clearLogButton = new Button { Content = "Log-Datei leeren" };
            clearLogButton.Click += (_, __) =>
            {
                Logger.Clear();
            };
            logButtonsPanel.Children.Add(clearLogButton);

            maintenanceContent.Children.Add(logButtonsPanel);

            var resetButton = new Button
            {
                Content = "Einstellungen zurücksetzen",
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            resetButton.Click += async (_, __) =>
            {
                var confirmed = await ConfirmResetAsync();
                if (!confirmed) return;

                ApplyAutoStart(false);

                _settings = new AppSettings();
                _settings.Save();

                ApplyTheme(_settings.DarkMode);
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
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Content = panel
            };

            var contentHost = new Grid { Padding = new Thickness(24, 16, 24, 24) };
            contentHost.Children.Add(scrollViewer);
            Grid.SetRow(contentHost, 1);

            var divider = MakeTitleBarDivider();
            Grid.SetRow(divider, 0);

            root.Children.Add(contentHost);
            root.Children.Add(divider);

            _settingsWindow.Content = root;
            _settingsWindow.Activate();

            // Schmaler passend zur Kartenbreite, Höhe bleibt wie gehabt
            StyleDarkWindow(_settingsWindow, 460, 620);
        }

        private async void ContactButton_Click(object sender, RoutedEventArgs e)
        {
            // Platzhalter-Text - hier einfach deine echten Kontaktdaten eintragen
            // (E-Mail, Discord, GitHub, o.ä.)
            var dialog = new ContentDialog
            {
                Title = "Kontakt",
                Content = "Fragen, Feedback oder Bugs?\n\n" +
                          "E-Mail: deine-email@beispiel.de\n" +
                          "Discord: dein-discord-tag\n" +
                          "GitHub: github.com/dein-name/winvora\n\n" +
                          "(Diesen Text in ContactButton_Click in MainWindow.xaml.cs anpassen)",
                CloseButtonText = "Schließen",
                XamlRoot = this.Content.XamlRoot
            };

            await dialog.ShowAsync();
        }

        private void ChangelogButton_Click(object sender, RoutedEventArgs e)
        {
            _changelogWindow = new Window
            {
                Title = "WinVora Changelog"
            };

            var root = new Grid
            {
                Background = new AcrylicBrush
                {
                    TintColor = _isDarkTheme ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White,
                    TintOpacity = 0.75,
                    FallbackColor = _isDarkTheme ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White
                }
            };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RequestedTheme = _isDarkTheme ? ElementTheme.Dark : ElementTheme.Light;

            var panel = new StackPanel
            {
                Spacing = 14
            };

            panel.Children.Add(new TextBlock
            {
                Text = "WinVora Changelog",
                FontSize = 28,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (SolidColorBrush)RootGrid.Resources["AppForegroundBrush"]
            });

            panel.Children.Add(MakeChangelogCard(
    "Version 0.3.3",
    "• Dateien-Seite: 20 Kategorien in 5 ausklappbare Gruppen sortiert\n" +
    "• Neue Einstellung: Startseite frei wählbar (Übersicht/System/Apps/Dateien)\n" +
    "• Neue Einstellung: Aktualisierungsintervall für CPU/RAM (1/2/5 Sekunden)\n" +
    "• Neue Einstellung: Mit Windows starten (Autostart)\n" +
    "• Neue Einstellung: Bestätigung vor dem Löschen ein-/ausschaltbar\n" +
    "• Neu: Log-Datei direkt aus den Einstellungen öffnen/leeren\n" +
    "• Neu: Einstellungen mit einem Klick zurücksetzen\n" +
    "• Neuer Kontakt-Button in der Sidebar (unter Version)\n" +
    "• Glas-Intensität jetzt fest auf 18 statt einstellbar\n" +
    "• Einstellungs- und Changelog-Fenster: passende Größe + scrollbar"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.3.2",
    "• Projekt komplett auf WinVora umbenannt (Namespace, exe, Fenstertitel)\n" +
    "• Eigenes App-Icon für Titelleiste und Taskleiste\n" +
    "• Dünne Trennlinie unter der Titelleiste für einen saubereren oberen Rand\n" +
    "• Glas-Karten starten jetzt unterhalb der Fenster-Buttons statt darunter\n" +
    "• Winget: Downloadgröße hat jetzt Vorrang vor Installationsgröße\n" +
    "• Warnhinweis, falls Chrome/Edge beim Löschen des Browser-Cache noch laufen\n" +
    "• Neues Logging (%LOCALAPPDATA%\\WinVora\\log.txt) für Fehler und Aktionen\n" +
    "• Globaler Fehler-Handler, damit stille Abstürze nachvollziehbar werden\n" +
    "• Self-Contained Single-File-Publish (keine Installation beim Testen nötig)\n" +
    "• Admin-Manifest entfernt - App startet ohne UAC-Abfrage\n" +
    "• publish.bat: baut und zippt die Testversion automatisch"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.3.1",
    "• Neu: Heller Modus (Umschalter in den Einstellungen)\n" +
    "• Einstellungen-Button jetzt über statt neben der Versions-Karte\n" +
    "• Winget-Liste läuft im Hintergrund - Oberfläche ruckelt beim Laden nicht mehr\n" +
    "• Refresh- und Start-Update-Button bei Winget einheitlich groß"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.3.0",
    "• Neue Dateien-Seite (Speicherbereinigung) mit 19 Kategorien\n" +
    "• Auswahl per Toggle, Einzel- und Sammel-Löschung\n" +
    "• \"Alle auswählen\"-Button auf der Dateien-Seite\n" +
    "• Bestätigungsdialog vor jeder Löschung\n" +
    "• Fortschrittsanzeige mit Live-Status beim Bereinigen\n" +
    "• Winget: Herausgeber und Größe werden automatisch nachgeladen\n" +
    "• Winget: Download-Fortschritt in MB beim Installieren\n" +
    "• Winget: klare Fehlermeldung, falls winget nicht installiert ist\n" +
    "• App startet automatisch mit Administratorrechten\n" +
    "• Eigene dunkle Titelleiste statt weißer System-Leiste\n" +
    "• Hintergrund auf reines Schwarz umgestellt\n" +
    "• Karten in kräftigerem Liquid-Glass-Weiß\n" +
    "• Echtes Mica-Backdrop mit Acrylic-Fallback\n" +
    "• Hover-Effekte auf den Info-Karten\n" +
    "• Sanftes Einblenden beim Seitenwechsel\n" +
    "• Ladebildschirm beim App-Start\n" +
    "• Diverse Bugfixes (doppeltes Laden der Systeminfos behoben)"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.2.0",
    "• Neue Übersicht als Startseite\n" +
    "• Systeminfo, Winget und Dateien als eigene Bereiche\n" +
    "• Große Health-Karten für CPU, RAM, Sicherheit und Updates\n" +
    "• Modernisierte Liquid-Glass-Oberfläche\n" +
    "• Größere Sidebar-Navigation\n" +
    "• Neue große Systeminfo-Dropdowns\n" +
    "• Alle Systeminfo-Kategorien sind einklappbar\n" +
    "• Alles-aufklappen- und Alles-einklappen-Buttons\n" +
    "• Systeminfo-Karten pro Kategorie zusammengefasst\n" +
    "• Größere Schrift, mehr Abstand und bessere Lesbarkeit\n" +
    "• Changelog-Fenster im Liquid-Glass-Stil\n" +
    "• Winget-Prozesshandling verbessert"
));

            panel.Children.Add(MakeChangelogCard(
                "Version 0.1.0",
                "• Schnellere Ladezeit\n" +
                "• CPU-Optimierung\n" +
                "• Live-Systeminfos\n" +
                "• Winget-Updateübersicht\n" +
                "• Erstes Changelog-Fenster"
            ));

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = panel
            };

            var contentHost = new Grid { Padding = new Thickness(24, 16, 24, 24) };
            contentHost.Children.Add(scrollViewer);
            Grid.SetRow(contentHost, 1);

            var divider = MakeTitleBarDivider();
            Grid.SetRow(divider, 0);

            root.Children.Add(contentHost);
            root.Children.Add(divider);

            _changelogWindow.Content = root;
            _changelogWindow.Activate();

            // Angenehme Startgröße mit Scroll-Reserve für künftige Einträge
            StyleDarkWindow(_changelogWindow, 560, 720);
        }

        private Border MakeChangelogCard(string title, string text)
        {
            var card = new Border
            {
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(16),
                Background = (SolidColorBrush)RootGrid.Resources["AppOverlay22"],
                BorderBrush = (SolidColorBrush)RootGrid.Resources["AppOverlay30"],
                BorderThickness = new Thickness(1)
            };

            var content = new StackPanel
            {
                Spacing = 10
            };

            content.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 18,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (SolidColorBrush)RootGrid.Resources["AppForegroundBrush"]
            });

            content.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (SolidColorBrush)RootGrid.Resources["AppForegroundCC"]
            });

            card.Child = content;
            return card;
        }

        private async Task LoadSystemSnapshotIfNeededAsync(string loadingText, string errorPrefix)
        {
            if (_cachedSnapshot != null)
            {
                ApplySnapshot(_cachedSnapshot);
                StartLiveUsageTimer();
                return;
            }

            if (_isLoadingSnapshot)
                return;

            _isLoadingSnapshot = true;
            PageSubtitle.Text = loadingText;
            UpdatesLoadingRing.IsActive = true;
            UpdatesLoadingRing.Visibility = Visibility.Visible;

            try
            {
                _cachedSnapshot = await SystemInfoProvider.GetFullSnapshotAsync();
                ApplySnapshot(_cachedSnapshot);
                PageSubtitle.Text = "";
                StartLiveUsageTimer();
            }
            catch (Exception ex)
            {
                PageSubtitle.Text = $"{errorPrefix}: {ex.Message}";
                Logger.LogError("LoadSystemSnapshotIfNeededAsync", ex);
            }
            finally
            {
                _isLoadingSnapshot = false;
                UpdatesLoadingRing.IsActive = false;
                UpdatesLoadingRing.Visibility = Visibility.Collapsed;
            }
        }

        private async void Overview_Click(object sender, RoutedEventArgs e)
        {
            SetPage("Übersicht");
            await LoadSystemSnapshotIfNeededAsync(
                "Systemstatus wird geladen...",
                "Fehler beim Laden der Übersicht");

            // BUGFIX: Vorher wurde bei jedem Aufruf der Übersicht "winget upgrade"
            // komplett neu gestartet (spürbar langsam), nur um die Update-Anzahl
            // in der Health-Karte zu zeigen. Jetzt wird ein bereits vorhandenes
            // Ergebnis wiederverwendet; nur beim allerersten Aufruf oder nach einem
            // expliziten Refresh auf der Winget-Seite wird tatsächlich neu geladen.
            if (_cachedPackages != null)
            {
                HealthUpdatesText.Text = _cachedPackages.Count == 0 ? "Keine" : _cachedPackages.Count.ToString();
                return;
            }

            HealthUpdatesText.Text = "Prüfe...";

            try
            {
                await LoadWinget();
            }
            catch (Exception ex)
            {
                PageSubtitle.Text = $"Fehler beim Laden der Übersicht: {ex.Message}";
                Logger.LogError("Overview_Click/LoadWinget", ex);
                return;
            }

            PageSubtitle.Text = "";
        }

        // BUGFIX: Der Systeminfo-Snapshot wurde einmal geladen und danach nie
        // wieder aktualisiert (außer den Live-Werten für CPU/RAM). Neue Laufwerke,
        // Netzwerkänderungen usw. wurden erst nach einem App-Neustart sichtbar.
        private async void RefreshSystemInfo_Click(object sender, RoutedEventArgs e)
        {
            _cachedSnapshot = null;
            await LoadSystemSnapshotIfNeededAsync(
                "Wird aktualisiert...",
                "Fehler beim Aktualisieren der Systeminfos");
        }

        private void ExpandAllSystem_Click(object sender, RoutedEventArgs e)
        {
            SetSystemExpanders(true);
        }

        private void CollapseAllSystem_Click(object sender, RoutedEventArgs e)
        {
            SetSystemExpanders(false);
        }

        private void SetSystemExpanders(bool isExpanded)
        {
            DeviceExpander.IsExpanded = isExpanded;
            OsExpander.IsExpanded = isExpanded;
            CpuExpander.IsExpanded = isExpanded;
            RamExpander.IsExpanded = isExpanded;
            BoardExpander.IsExpanded = isExpanded;
            SecurityExpander.IsExpanded = isExpanded;
            GpuExpander.IsExpanded = isExpanded;
            DrivesExpander.IsExpanded = isExpanded;
            NetworkExpander.IsExpanded = isExpanded;
            BatteryExpander.IsExpanded = isExpanded;
        }

        // ================= SYSTEM =================

        private async void System_Click(object sender, RoutedEventArgs e)
        {
            SetPage("System");
            await LoadSystemSnapshotIfNeededAsync(
                "Wird geladen...",
                "Fehler beim Laden der Systeminfos");
        }

        private void ApplySnapshot(SystemInfoSnapshot s)
        {
            SysComputerName.Text = s.ComputerName;
            SysUserName.Text = s.UserName;
            SysManufacturerModel.Text = $"{s.Manufacturer} {s.Model}".Trim();
            SysSerialNumber.Text = s.SerialNumber;
            SysArchitecture.Text = s.Architecture;

            SysEdition.Text = s.WindowsEdition;
            SysVersionBuild.Text = $"{s.WindowsVersion} (Build {s.BuildNumber})";
            SysInstallDate.Text = s.InstallDate;
            SysLastUpdate.Text = string.IsNullOrEmpty(s.LastUpdate) ? "N/A" : s.LastUpdate;
            SysActivation.Text = s.ActivationStatus;
            SysUptime.Text = s.Uptime;
            SysDotNet.Text = s.DotNetVersion;
            SysDirectX.Text = s.DirectXVersion;

            SysCpuName.Text = s.CpuName;
            SysCpuDetails.Text = $"{s.CpuCores} Kerne / {s.CpuThreads} Threads / {s.CpuClock}";

            SysRamDetails.Text = $"{s.RamTotal} installiert, {s.RamUsed} belegt, {s.RamFree} frei";

            SysMainboard.Text = s.Mainboard;
            SysBios.Text = s.BiosVersion;

            SysSecureBoot.Text = s.SecureBoot;
            SysTpm.Text = s.TpmVersion;
            SysVirtualization.Text = s.Virtualization;
            SysDefender.Text = s.DefenderStatus;
            SysFirewall.Text = s.FirewallStatus;
            SysBitLocker.Text = s.BitLockerStatus;

            SysGpuPanel.Children.Clear();
            if (s.Gpus.Length == 0)
            {
                SysGpuPanel.Children.Add(MakeInfoCard("Keine GPU erkannt", ""));
            }
            foreach (var gpu in s.Gpus)
            {
                SysGpuPanel.Children.Add(MakeInfoCard(gpu, "Grafikkarte"));
            }

            SysDrivesPanel.Children.Clear();
            foreach (var drive in s.Drives)
            {
                SysDrivesPanel.Children.Add(MakeInfoCard(drive.Name, drive.TotalSize, $"{drive.FreeSpace} frei"));
            }

            SysNetworkPanel.Children.Clear();
            if (s.NetworkAdapters.Length == 0)
            {
                SysNetworkPanel.Children.Add(MakeInfoCard("Kein aktiver Netzwerkadapter gefunden", ""));
            }
            foreach (var net in s.NetworkAdapters)
            {
                SysNetworkPanel.Children.Add(MakeInfoCard(
                    net.Name,
                    $"IPv4: {net.IPv4}  •  MAC: {net.MacAddress}",
                    $"Gateway: {net.Gateway}\nDNS: {net.Dns}"));
            }

            SysBattery.Text = s.BatteryStatus;
            var defenderOk = s.DefenderStatus.Contains("Aktiv", StringComparison.OrdinalIgnoreCase);
            var firewallOk = s.FirewallStatus.Contains("Aktiv", StringComparison.OrdinalIgnoreCase);

            HealthSecurityText.Text = (defenderOk, firewallOk) switch
            {
                (true, true) => "Aktiv",
                (false, true) => "Defender prüfen",
                (true, false) => "Firewall prüfen",
                _ => "Prüfen"
            };
        }

        // Kleine Hilfsmethode, um schnell eine SettingsCard mit Header/Beschreibung/Inhalt zu bauen
        private Border MakeInfoCard(string header, string description, string? content = null)
        {
            var item = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MinHeight = 105,
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(22),
                Background = (SolidColorBrush)RootGrid.Resources["AppOverlay18"],
                BorderBrush = (SolidColorBrush)RootGrid.Resources["AppOverlay28"],
                BorderThickness = new Thickness(1)
            };

            // Leichter Hover-Effekt: Karte hellt sich beim Überfahren mit der Maus auf.
            item.PointerEntered += (_, __) =>
                item.Background = (SolidColorBrush)RootGrid.Resources["AppOverlay28"];
            item.PointerExited += (_, __) =>
                item.Background = (SolidColorBrush)RootGrid.Resources["AppOverlay18"];

            var panel = new StackPanel
            {
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            panel.Children.Add(new TextBlock
            {
                Text = header,
                FontSize = 17,
                Foreground = (SolidColorBrush)RootGrid.Resources["AppForegroundBrush"],
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });

            if (!string.IsNullOrWhiteSpace(description))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = description,
                    FontSize = 15,
                    Foreground = (SolidColorBrush)RootGrid.Resources["AppForegroundC0"],
                    TextWrapping = TextWrapping.Wrap
                });
            }

            if (!string.IsNullOrWhiteSpace(content))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = content,
                    FontSize = 15,
                    Foreground = (SolidColorBrush)RootGrid.Resources["AppForegroundD8"],
                    TextWrapping = TextWrapping.Wrap
                });
            }

            item.Child = panel;
            return item;
        }



        private void StartLiveUsageTimer()
        {
            _liveUsageTimer?.Stop();
            _liveUsageTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(_settings.LiveUpdateIntervalSeconds) };

            _liveUsageTimer.Tick += async (_, __) =>
            {
                // Läuft im Hintergrund, damit der UI-Thread (und damit das
                // Scrollen) nicht alle 2 Sekunden kurz blockiert wird.
                var (cpu, ram, _) = await Task.Run(() => SystemInfoProvider.GetLiveUsage());

                SysCpuUsageBar.Value = cpu;
                SysCpuUsageText.Text = $"{cpu}%";

                SysRamUsageBar.Value = ram;
                SysRamUsageText.Text = $"{ram}%";

                HealthCpuText.Text = $"{cpu}%";
                HealthRamText.Text = $"{ram}%";
            };

            _liveUsageTimer.Start();
        }

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
            if (_wingetRows.Count == 0) return;

            bool allSelected = _wingetRows.All(r => r.Toggle.IsOn);
            bool newState = !allSelected;

            foreach (var row in _wingetRows)
                row.Toggle.IsOn = newState;

            WingetSelectAllButton.Content = newState ? "Alle abwählen" : "Alle auswählen";
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadWinget(forceRefresh: true);
        }

        private async Task LoadWinget(bool forceRefresh = false)
        {
            // BUGFIX (Teil 2): Wenn schon ein Ergebnis vorliegt und kein
            // erzwungener Refresh angefordert wurde, einfach das gecachte
            // Ergebnis erneut anzeigen statt "winget upgrade" neu zu starten.
            if (!forceRefresh && _cachedPackages != null)
            {
                RenderWingetPackages(_cachedPackages);
                return;
            }

            ContentArea.Children.Clear();
            _wingetRows.Clear();
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

                    bool hasStartedRows = false;
                    string? line;

                    while ((line = p.StandardOutput.ReadLine()) != null)
                    {
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
                UpdatesLoadingRing.IsActive = false;
                UpdatesLoadingRing.Visibility = Visibility.Collapsed;
                RefreshButton.IsEnabled = true;
                StartUpdateButton.IsEnabled = true;
            }

            if (hadError)
            {
                if (wingetNotFound)
                {
                    PageSubtitle.Text = "winget wurde nicht gefunden";
                    HealthUpdatesText.Text = "N/A";

                    ContentArea.Children.Add(new TextBlock
                    {
                        Text = "winget ist nicht installiert oder nicht im PATH verfügbar. " +
                               "Installiere den \"App Installer\" (Windows-Paketmanager) über den Microsoft Store " +
                               "und starte WinVora danach neu.",
                        Foreground = new SolidColorBrush(Microsoft.UI.Colors.OrangeRed),
                        TextWrapping = TextWrapping.Wrap
                    });
                }
                else
                {
                    ContentArea.Children.Add(new TextBlock
                    {
                        Text = $"Fehler beim Ausführen von winget: {errorMessage}",
                        Foreground = new SolidColorBrush(Microsoft.UI.Colors.OrangeRed)
                    });
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

            if (packages.Count == 0)
            {
                PageSubtitle.Text = "Keine Updates verfügbar";
                HealthUpdatesText.Text = "Keine";
                WingetSelectAllButton.Content = "Alle abwählen";

                ContentArea.Children.Add(new TextBlock
                {
                    Text = "Keine Updates gefunden.",
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray)
                });
                return;
            }

            PageSubtitle.Text = packages.Count == 1
                ? "1 App hat ein Update"
                : $"{packages.Count} Apps haben Updates";

            HealthUpdatesText.Text = packages.Count.ToString();

            // Pakete starten standardmäßig alle ausgewählt (IsOn = true weiter
            // unten) - der Button muss also mit "Alle abwählen" starten.
            WingetSelectAllButton.Content = "Alle abwählen";

            foreach (var pkg in packages)
            {
                var toggle = new ToggleSwitch { IsOn = true, OnContent = "", OffContent = "" };
                var baseDescription = $"{pkg.Id}  •  {pkg.Version} → {pkg.Available}  •  {pkg.Source}";

                var card = new ToolkitControls.SettingsCard
                {
                    Header = pkg.Name,
                    Description = $"{baseDescription}  •  Herausgeber: wird geladen...  •  Größe: wird geladen...",
                    HeaderIcon = new FontIcon { Glyph = "\uE7B8" }, // Platzhalter-App-Icon
                    Content = toggle
                };

                ContentArea.Children.Add(card);
                _wingetRows.Add((pkg, toggle, card, baseDescription));
            }

            // Herausgeber und Größe laufen im Hintergrund nach (winget show pro Paket),
            // damit die Liste sofort erscheint und nicht auf alle Detailabfragen wartet.
            _ = LoadWingetDetailsInBackground(_wingetRows.ToList());
        }

        // BUGFIX (Lag-Problem): Vorher liefen bis zu 4 "winget show"-Prozesse
        // gleichzeitig UND jedes einzelne Ergebnis hat sofort für sich ein
        // UI-Update (Card.Description) samt Relayout ausgelöst. Bei vielen
        // Updates kamen so kurz hintereinander viele einzelne Relayouts der
        // gesamten Liste zusammen - das war der spürbare Ruckler beim Öffnen
        // von Winget. Jetzt: weniger parallele Prozesse UND alle fertigen
        // Ergebnisse werden gesammelt und nur alle 300ms in einem Rutsch
        // angewendet, statt sofort bei jedem einzelnen Treffer.
        private async Task LoadWingetDetailsInBackground(
            List<(WingetPackage Package, ToggleSwitch Toggle, ToolkitControls.SettingsCard Card, string BaseDescription)> rows)
        {
            using var semaphore = new SemaphoreSlim(2);

            var pending = new System.Collections.Concurrent.ConcurrentQueue<(ToolkitControls.SettingsCard Card, string Text)>();

            void FlushPending()
            {
                while (pending.TryDequeue(out var item))
                    item.Card.Description = item.Text;
            }

            var flushTimer = DispatcherQueue.CreateTimer();
            flushTimer.Interval = TimeSpan.FromMilliseconds(300);
            flushTimer.IsRepeating = true;
            flushTimer.Tick += (_, __) => FlushPending();
            flushTimer.Start();

            try
            {
                var tasks = rows.Select(async row =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        var (publisher, size) = await GetWingetDetailsAsync(row.Package.Id);
                        pending.Enqueue((row.Card,
                            $"{row.BaseDescription}  •  Herausgeber: {publisher}  •  Größe: {size}"));
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                await Task.WhenAll(tasks);
            }
            finally
            {
                flushTimer.Stop();
                FlushPending(); // letzte übrig gebliebene Ergebnisse noch anwenden
            }
        }

        // Liest "winget show --id X" aus und sucht sprachunabhängig nach
        // Herausgeber- und Größenangaben. Das genaue Textformat kann je nach
        // winget-Version/Sprache leicht variieren.
        private async Task<(string Publisher, string Size)> GetWingetDetailsAsync(string packageId)
        {
            string publisher = "N/A";
            string size = "N/A";

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "winget",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                psi.ArgumentList.Add("show");
                psi.ArgumentList.Add("--id");
                psi.ArgumentList.Add(packageId);
                psi.ArgumentList.Add("--accept-source-agreements");
                psi.ArgumentList.Add("--disable-interactivity");

                using var p = new Process { StartInfo = psi };
                p.Start();

                var foundDownloadSize = false;

                var outputTask = Task.Run(async () =>
                {
                    while (!p.StandardOutput.EndOfStream)
                    {
                        var line = await p.StandardOutput.ReadLineAsync();
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        var colonIndex = line.IndexOf(':');
                        if (colonIndex < 0) continue;

                        var key = line[..colonIndex].Trim();
                        var value = line[(colonIndex + 1)..].Trim();
                        if (string.IsNullOrWhiteSpace(value)) continue;

                        if (key.Contains("Publisher", StringComparison.OrdinalIgnoreCase) ||
                            key.Contains("Herausgeber", StringComparison.OrdinalIgnoreCase))
                        {
                            publisher = value;
                        }
                        else if (key.Contains("Download Size", StringComparison.OrdinalIgnoreCase) ||
                                 key.Contains("Downloadgröße", StringComparison.OrdinalIgnoreCase))
                        {
                            // Echte Downloadgröße hat immer Vorrang und darf nicht
                            // durch eine später gefundene Installationsgröße
                            // überschrieben werden.
                            size = value;
                            foundDownloadSize = true;
                        }
                        else if (!foundDownloadSize &&
                                 (key.Contains("Größe", StringComparison.OrdinalIgnoreCase) ||
                                  (key.Contains("Size", StringComparison.OrdinalIgnoreCase) &&
                                   !key.Contains("Installer", StringComparison.OrdinalIgnoreCase))))
                        {
                            // Fallback: irgendeine andere Größenangabe (z.B.
                            // Installationsgröße), falls keine Downloadgröße
                            // gefunden wird - besser als "N/A".
                            size = value;
                        }
                    }
                });

                // BUGFIX: StandardError wurde vorher nie gelesen, obwohl
                // RedirectStandardError=true gesetzt ist. Läuft der Puffer voll,
                // blockiert der Kindprozess dauerhaft (Deadlock-Risiko). Jetzt
                // wird der Error-Stream parallel mitgelesen und verworfen.
                var errorTask = Task.Run(async () =>
                {
                    while (!p.StandardError.EndOfStream)
                        await p.StandardError.ReadLineAsync();
                });

                await Task.WhenAll(outputTask, errorTask, p.WaitForExitAsync());
            }
            catch
            {
                // Best effort - bleibt bei "N/A", falls winget show fehlschlägt
            }

            return (publisher, size);
        }

        private async void StartUpdate_Click(object sender, RoutedEventArgs e)
        {
            var selected = _wingetRows.Where(r => r.Toggle.IsOn).Select(r => r.Package).ToList();

            if (selected.Count == 0)
            {
                UpdateProgressPanel.Visibility = Visibility.Visible;
                UpdateProgressText.Text = "Keine Pakete ausgewählt.";
                UpdateProgressBar.Value = 0;
                return;
            }

            RefreshButton.IsEnabled = false;
            StartUpdateButton.IsEnabled = false;

            UpdateProgressPanel.Visibility = Visibility.Visible;
            UpdateProgressBar.Maximum = selected.Count;
            UpdateProgressBar.Value = 0;

            var failed = new List<string>();

            var progress = new Progress<(string Text, double? Percent)>(p =>
            {
                CurrentPackageStatusText.Text = p.Text;

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
                UpdateProgressText.Text = $"Installiere {pkg.Name} ({i + 1}/{selected.Count})...";
                CurrentPackageStatusText.Text = "";
                CurrentPackageProgressBar.IsIndeterminate = true;
                CurrentPackageProgressBar.Value = 0;

                var success = await RunWingetUpgrade(pkg.Id, progress);
                if (!success)
                    failed.Add(pkg.Name);

                CurrentPackageProgressBar.IsIndeterminate = false;
                CurrentPackageProgressBar.Value = 100;
                UpdateProgressBar.Value = i + 1;
            }

            UpdateProgressText.Text = failed.Count == 0
                ? "Alle ausgewählten Updates wurden installiert."
                : $"Fertig mit Fehlern bei: {string.Join(", ", failed)}";
            CurrentPackageStatusText.Text = "";

            // Kurz die Abschlussmeldung stehen lassen, dann automatisch neu laden
            await Task.Delay(2000);
            UpdateProgressPanel.Visibility = Visibility.Collapsed;

            // Nach einer Installation ist der Cache veraltet - erzwungener Reload.
            _cachedPackages = null;
            await LoadWinget(forceRefresh: true);
        }

        // Erkennt Zeilen wie "50% 12,3 MB / 24,6 MB" oder nur "12.3 MB / 24.6 MB"
        // (Format kann je nach winget-Version/Sprache leicht abweichen).
        private static readonly Regex ProgressWithPercentRegex = new(
            @"(\d{1,3})\s*%.*?([\d.,]+\s?[KMGT]?B)\s*/\s*([\d.,]+\s?[KMGT]?B)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex ProgressSizeOnlyRegex = new(
            @"([\d.,]+\s?[KMGT]?B)\s*/\s*([\d.,]+\s?[KMGT]?B)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private async Task<bool> RunWingetUpgrade(string packageId, IProgress<(string Text, double? Percent)> progress)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "winget",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                psi.ArgumentList.Add("upgrade");
                psi.ArgumentList.Add("--id");
                psi.ArgumentList.Add(packageId);
                psi.ArgumentList.Add("--silent");
                psi.ArgumentList.Add("--accept-package-agreements");
                psi.ArgumentList.Add("--accept-source-agreements");
                psi.ArgumentList.Add("--disable-interactivity");

                using var p = new Process { StartInfo = psi };

                p.Start();

                var outputTask = Task.Run(async () =>
                {
                    while (!p.StandardOutput.EndOfStream)
                    {
                        var line = await p.StandardOutput.ReadLineAsync();
                        if (!string.IsNullOrWhiteSpace(line))
                            ReportIfProgress(line, progress);
                    }
                });

                var errorTask = Task.Run(async () =>
                {
                    while (!p.StandardError.EndOfStream)
                        await p.StandardError.ReadLineAsync();
                });

                await Task.WhenAll(outputTask, errorTask, p.WaitForExitAsync());

                return p.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        private void ReportIfProgress(string line, IProgress<(string Text, double? Percent)> progress)
        {
            var withPercent = ProgressWithPercentRegex.Match(line);
            if (withPercent.Success)
            {
                var pct = double.Parse(withPercent.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                var downloaded = withPercent.Groups[2].Value.Trim();
                var total = withPercent.Groups[3].Value.Trim();
                progress.Report(($"{downloaded} / {total}", pct));
                return;
            }

            var sizeOnly = ProgressSizeOnlyRegex.Match(line);
            if (sizeOnly.Success)
            {
                var downloaded = sizeOnly.Groups[1].Value.Trim();
                var total = sizeOnly.Groups[2].Value.Trim();
                progress.Report(($"{downloaded} / {total}", null));
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
            if (columns == null) return null;

            string Slice(int i)
            {
                if (i >= columns.Length) return "";

                int start = columns[i];
                int end = i + 1 < columns.Length ? columns[i + 1] : line.Length;

                if (start < 0 || start >= line.Length) return "";
                end = Math.Max(start, Math.Min(end, line.Length)); // verhindert negative Länge

                return line.Substring(start, end - start).Trim();
            }

            var pkg = new WingetPackage
            {
                Name = Slice(0),
                Id = Slice(1),
                Version = Slice(2),
                Available = Slice(3),
                Source = Slice(4)
            };

            return string.IsNullOrWhiteSpace(pkg.Name) ? null : pkg;
        }

        // ================= STORAGE =================

        private async void Cleaner_Click(object sender, RoutedEventArgs e)
        {
            SetPage("Storage");
            await LoadStorage();
        }

        private async void StorageRefresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadStorage();
        }

        private void StorageSelectAll_Click(object sender, RoutedEventArgs e)
        {
            if (_storageRows.Count == 0) return;

            bool allSelected = _storageRows.All(r => r.Toggle.IsOn);
            bool newState = !allSelected;

            foreach (var row in _storageRows)
                row.Toggle.IsOn = newState;

            StorageSelectAllButton.Content = newState ? "Alle abwählen" : "Alle auswählen";
        }

        private async Task LoadStorage()
        {
            StoragePanel.Children.Clear();
            _storageRows.Clear();

            StorageRefreshButton.IsEnabled = false;
            StorageDeleteSelectedButton.IsEnabled = false;

            UpdatesLoadingRing.Visibility = Visibility.Visible;
            UpdatesLoadingRing.IsActive = true;

            List<StorageCategory> categories;

            try
            {
                categories = await StorageService.GetCategoriesWithSizesAsync();
            }
            catch (Exception ex)
            {
                Logger.LogError("LoadStorage", ex);
                StoragePanel.Children.Add(new TextBlock
                {
                    Text = $"Fehler beim Ermitteln der Speicherbelegung: {ex.Message}",
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.OrangeRed)
                });
                return;
            }
            finally
            {
                UpdatesLoadingRing.IsActive = false;
                UpdatesLoadingRing.Visibility = Visibility.Collapsed;
                StorageRefreshButton.IsEnabled = true;
                StorageDeleteSelectedButton.IsEnabled = true;
            }

            long totalBytes = categories.Sum(c => c.SizeBytes);
            PageSubtitle.Text = $"Insgesamt {StorageService.FormatBytes(totalBytes)} durch Bereinigung freigebbar";
            StorageSelectAllButton.Content = "Alle auswählen";

            var byKey = categories.ToDictionary(c => c.Key);

            // Gruppiert die Kategorien thematisch, damit nicht 20 einzelne
            // Karten untereinander stehen, sondern ausklappbare Abschnitte
            // (gleiches Prinzip wie bei den Systeminfo-Kategorien).
            var groups = new (string Title, string[] Keys)[]
            {
                ("Temporäre Dateien", new[] { "user_temp", "windows_temp", "prefetch", "inet_cache" }),
                ("Papierkorb & Downloads", new[] { "recycle_bin", "update_cache", "delivery_optimization", "upgrade_logs", "old_install_files" }),
                ("System-Caches", new[] { "dx_shader_cache", "thumbnail_cache", "store_cache", "dns_cache" }),
                ("Fehlerberichte & Logs", new[] { "wer", "minidump", "crash_dumps", "logs", "setup_logs", "defender_temp" }),
                ("Browser", new[] { "browser_cache" }),
            };

            foreach (var group in groups)
            {
                var groupCategories = group.Keys.Where(byKey.ContainsKey).Select(k => byKey[k]).ToList();
                if (groupCategories.Count == 0) continue;

                long groupBytes = groupCategories.Sum(c => c.SizeBytes);

                var expander = new Expander
                {
                    Header = $"{group.Title}  •  {StorageService.FormatBytes(groupBytes)}",
                    IsExpanded = false,
                    FontSize = 18,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    MinHeight = 56,
                    Padding = new Thickness(4),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Margin = new Thickness(0, 0, 0, 4)
                };

                var groupPanel = new StackPanel { Spacing = 4, Margin = new Thickness(4) };

                foreach (var category in groupCategories)
                {
                    groupPanel.Children.Add(MakeStorageCard(category));
                }

                expander.Content = groupPanel;
                StoragePanel.Children.Add(expander);
            }
        }

        private ToolkitControls.SettingsCard MakeStorageCard(StorageCategory category)
        {
            var toggle = new ToggleSwitch { IsOn = false, OnContent = "", OffContent = "" };

            var deleteButton = new Button { Content = "Löschen" };
            deleteButton.Click += async (_, __) => await DeleteSingleCategory(category, deleteButton);

            var actionsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                VerticalAlignment = VerticalAlignment.Center
            };
            actionsPanel.Children.Add(toggle);
            actionsPanel.Children.Add(deleteButton);

            var descriptionSuffix = category.RequiresAdmin ? "  •  benötigt evtl. Admin-Rechte" : "";

            var card = new ToolkitControls.SettingsCard
            {
                Header = category.Name,
                Description = $"{category.Description}{descriptionSuffix}  •  {category.SizeDisplay}",
                HeaderIcon = new FontIcon { Glyph = GetStorageIconGlyph(category.Key) },
                Content = actionsPanel
            };

            _storageRows.Add((category, toggle));
            return card;
        }

        // Ordnet jeder Storage-Kategorie ein passendes Fluent-Icon-Glyph zu.
        private static string GetStorageIconGlyph(string categoryKey) => categoryKey switch
        {
            "user_temp" or "windows_temp" => "\uE74D",       // Papierkorb-artiges Symbol für Temp
            "prefetch" => "\uE945",                          // Blitz / Performance
            "recycle_bin" => "\uE74D",                        // Papierkorb
            "dx_shader_cache" => "\uE7F4",                    // Grafikkarte
            "update_cache" or "delivery_optimization" => "\uE895", // Download/Update
            "wer" or "minidump" or "crash_dumps" => "\uE783", // Warnung
            "thumbnail_cache" => "\uEB9F",                    // Bilder
            "browser_cache" or "inet_cache" => "\uE774",      // Globus/Web
            "logs" or "setup_logs" or "upgrade_logs" => "\uE7C3", // Dokument
            "defender_temp" => "\uEA18",                      // Schild
            "store_cache" => "\uE719",                        // Store-Symbol
            "dns_cache" => "\uE968",                          // Netzwerk
            "old_install_files" => "\uE7B8",                  // Paket/App
            _ => "\uE8B7"                                     // Standard: Ordner
        };

        // Prüft, ob eine Kategorie betroffene Browser-Prozesse hat, die gerade
        // laufen - dann schlagen einzelne Dateien beim Löschen fehl, weil sie
        // in Benutzung sind. Liefert einen Warnhinweis oder "" falls nichts zu melden ist.
        private static string GetRunningProcessWarning(IEnumerable<StorageCategory> categories)
        {
            if (!categories.Any(c => c.Key == "browser_cache")) return "";

            var runningBrowsers = new List<string>();
            if (Process.GetProcessesByName("chrome").Length > 0) runningBrowsers.Add("Chrome");
            if (Process.GetProcessesByName("msedge").Length > 0) runningBrowsers.Add("Edge");

            if (runningBrowsers.Count == 0) return "";

            return $"\n\nHinweis: {string.Join(" und ", runningBrowsers)} läuft gerade - " +
                   "einige Cache-Dateien sind dadurch in Benutzung und werden übersprungen. " +
                   "Für eine vollständige Bereinigung den Browser vorher schließen.";
        }

        private async Task DeleteSingleCategory(StorageCategory category, Button sourceButton)
        {
            bool confirmed = await ConfirmAsync(
                "Bereich löschen?",
                $"\"{category.Name}\" wird bereinigt. Das kann nicht rückgängig gemacht werden. Fortfahren?" +
                GetRunningProcessWarning(new[] { category }));

            if (!confirmed) return;

            sourceButton.IsEnabled = false;
            StorageRefreshButton.IsEnabled = false;
            StorageDeleteSelectedButton.IsEnabled = false;

            StorageProgressPanel.Visibility = Visibility.Visible;
            StorageProgressBar.Maximum = 1;
            StorageProgressBar.Value = 0;
            StorageProgressText.Text = $"Lösche {category.Name}...";

            var (success, message) = await StorageService.DeleteCategoryAsync(category);
            Logger.Log($"Storage-Löschung '{category.Name}': {(success ? "OK" : "Fehler")} - {message}");

            StorageProgressBar.Value = 1;
            StorageProgressText.Text = success
                ? $"{category.Name}: {message}"
                : $"{category.Name} - Fehler: {message}";

            await Task.Delay(1500);
            StorageProgressPanel.Visibility = Visibility.Collapsed;

            await LoadStorage();
        }

        private async void StorageDeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            var selected = _storageRows.Where(r => r.Toggle.IsOn).Select(r => r.Category).ToList();

            if (selected.Count == 0)
            {
                StorageProgressPanel.Visibility = Visibility.Visible;
                StorageProgressText.Text = "Keine Bereiche ausgewählt.";
                StorageProgressBar.Value = 0;
                return;
            }

            bool confirmed = await ConfirmAsync(
                "Ausgewählte Bereiche löschen?",
                $"{selected.Count} Bereich(e) werden bereinigt: {string.Join(", ", selected.Select(c => c.Name))}. Das kann nicht rückgängig gemacht werden. Fortfahren?" +
                GetRunningProcessWarning(selected));

            if (!confirmed) return;

            StorageRefreshButton.IsEnabled = false;
            StorageDeleteSelectedButton.IsEnabled = false;

            StorageProgressPanel.Visibility = Visibility.Visible;
            StorageProgressBar.Maximum = selected.Count;
            StorageProgressBar.Value = 0;

            var results = new List<string>();

            for (int i = 0; i < selected.Count; i++)
            {
                var category = selected[i];
                StorageProgressText.Text = $"Lösche {category.Name} ({i + 1}/{selected.Count})...";

                var (success, message) = await StorageService.DeleteCategoryAsync(category);
                results.Add(success ? $"{category.Name}: OK" : $"{category.Name}: Fehler");
                Logger.Log($"Storage-Sammel-Löschung '{category.Name}': {(success ? "OK" : "Fehler")} - {message}");

                StorageProgressBar.Value = i + 1;
            }

            StorageProgressText.Text = "Bereinigung abgeschlossen: " + string.Join(", ", results);

            await Task.Delay(2500);
            StorageProgressPanel.Visibility = Visibility.Collapsed;

            await LoadStorage();
        }
    }

    public class WingetPackage
    {
        public string Name { get; set; } = "";
        public string Id { get; set; } = "";
        public string Version { get; set; } = "";
        public string Available { get; set; } = "";
        public string Source { get; set; } = "";
    }
}