using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
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
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.ViewManagement;
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
        private bool _isLoadingWinget;
        private bool _isUpdatingWinget;
        private bool _isLoadingStorage;
        private bool _isDeletingStorage;
        private bool _isLoadingPrograms;
        private bool _isUninstalling;
        private SecurityHealthState _securityHealthState = SecurityHealthState.Unknown;
        private string _lastAntivirusStatus = "Unbekannt";
        private string _lastFirewallStatus = "Unbekannt";
        private CancellationTokenSource? _wingetUpdateCancellation;
        private readonly CancellationTokenSource _startupCancellation = new();
        private readonly Stopwatch _startupOverlayLifetime = Stopwatch.StartNew();
        private readonly WingetUpdateService _wingetUpdateService = new();
        private List<WingetPackage>? _cachedPackages;
        private bool _isDarkTheme = true;
        private bool _wingetSelectAllState = true;
        private bool _isBulkUpdatingWingetSelection;

        private readonly List<(WingetPackage Package, CheckBox Toggle, ToolkitControls.SettingsCard Card, string BaseDescription)> _wingetRows = new();
        private readonly Dictionary<string, TextBlock> _wingetStatusBadges = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ProgressBar> _wingetCardProgressBars = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<(StorageCategory Category, CheckBox Toggle)> _storageRows = new();
        private TextBlock? _wingetNoResultsText;
        private TextBlock? _uninstallNoResultsText;
        private AppSettings _settings = AppSettings.Load();
        private Microsoft.UI.Xaml.Media.Animation.Storyboard? _startupHexStoryboard;
        private readonly UISettings _uiSettings = new();
        private CancellationTokenSource? _dashboardRefreshDebounce;
        private CancellationTokenSource? _wingetSearchDebounce;
        private CancellationTokenSource? _uninstallSearchDebounce;
        private CancellationTokenSource? _infoBarDismissCancellation;
        private CancellationTokenSource? _storageAnalysisCancellation;
        private bool _closePromptOpen;
        private Windows.Graphics.RectInt32? _postStartupWindowRect;

        // Eine zentrale Versionsquelle: <Version> in WinVora.csproj. So können
        // Sidebar, Einstellungen und Updatevergleich nicht mehr auseinanderlaufen.
        private static readonly string CurrentVersion =
            Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion
                .Split('+', 2)[0]
            ?? (Assembly.GetExecutingAssembly().GetName().Version is { } version
                ? version.Revision > 0
                    ? $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}"
                    : $"{version.Major}.{version.Minor}.{version.Build}"
                : "0.0.0");
        private static bool IsBetaBuild => CurrentVersion.Contains('-', StringComparison.OrdinalIgnoreCase);

        // Vom Hintergrund-Check gefundenes Update (falls vorhanden) - damit
        // das Einstellungen-Fenster nicht nochmal extra suchen muss.
        private UpdateInfo? _pendingUpdateInfo;
        private string _currentPageKey = "Übersicht";
        private string _historyFilter = "All";
        private readonly HashSet<string> _expandedHistoryEntries = new(StringComparer.Ordinal);
        private readonly Dictionary<string, double> _pageScrollOffsets = new();
        private PcChangeSummary? _pcChangeSummary;
        private Grid? _dashboardPrimaryGrid;
        private IReadOnlyList<LargeFolderResult> _cachedLargeFolders = Array.Empty<LargeFolderResult>();
        private DateTime? _largeFolderAnalysisUtc;
        private bool? _narrowLayoutState;
        private bool? _compactHeightState;
        private bool _securityDetailsLoaded;
        private bool _securityDetailsLoading;
        private Task? _initialBackgroundRefresh;


        public MainWindow()
        {
            var startupTimer = Stopwatch.StartNew();
            this.InitializeComponent();
            try
            {
                CoreLogicSelfTests.Run();
            }
            catch (Exception ex)
            {
                // Interne Debug-Prüfungen dürfen keinen unsichtbaren Prozess
                // ohne Hauptfenster hinterlassen. Der konkrete Testausdruck
                // steht im Protokoll und die App bleibt praktisch testbar.
                Logger.LogError("Interne Logiktests", ex);
            }
            SetupSystemInfoCopyButtons();
            SetupCompactTooltips();
            SecurityExpander.RegisterPropertyChangedCallback(Expander.IsExpandedProperty, (dependencyObject, property) =>
            {
                if (SecurityExpander.IsExpanded) _ = LoadSecurityDetailsAsync();
            });
            RootGrid.SizeChanged += (_, args) =>
            {
                ApplyResponsiveLayout(args.NewSize);
            };
            RootGrid.Loaded += (_, __) => RemoveRoundedDecorativeBorders(RootGrid);
            this.Title = "WinVora";
            NavVersionText.Text = $"Version {CurrentVersion}";
            NavBetaBadge.Visibility = IsBetaBuild ? Visibility.Visible : Visibility.Collapsed;
            UpdateUpdateChannelUi();
            this.Activated += MainWindow_Activated;
            this.AppWindow.Closing += MainWindow_Closing;
            this.Closed += (_, __) =>
            {
                _startupCancellation.Cancel();
                _startupCancellation.Dispose();
                _uiSettings.ColorValuesChanged -= SystemColorValuesChanged;
                _dashboardRefreshDebounce?.Cancel();
                _wingetSearchDebounce?.Cancel();
                _uninstallSearchDebounce?.Cancel();
                _infoBarDismissCancellation?.Cancel();
                _infoBarDismissCancellation?.Dispose();
                _storageAnalysisCancellation?.Cancel();
                _storageAnalysisCancellation?.Dispose();
                SaveWindowPlacement();
                HardwareMonitorService.Shutdown();
            };

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
            ApplyConfiguredColorScheme(persist: false);

            BuildDashboardHierarchy();
            NormalizeCardMetrics();
            SetupOverviewCardHoverEffects();

            Localization.CurrentLanguage = _settings.Language;
            ApplyLanguage();
            RestoreWindowPlacement();
            if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter startupPresenter)
            {
                startupPresenter.IsResizable = false;
                startupPresenter.IsMaximizable = false;
            }
            SetupKeyboardShortcuts();
            UpdateService.CleanupOldDownloads();
            _ = Task.Run(LegacyFeatureCleanup.RemoveMaintenanceTasksOnceAsync);
            _uiSettings.ColorValuesChanged += SystemColorValuesChanged;
            Logger.Log($"Hauptfenster initialisiert nach {startupTimer.ElapsedMilliseconds} ms; " +
                $"Arbeitsspeicher: {Process.GetCurrentProcess().WorkingSet64 / 1024d / 1024d:0.0} MB.");
        }

        private void SystemColorValuesChanged(UISettings sender, object args)
        {
            if (_settings.ColorScheme != "System") return;
            DispatcherQueue.TryEnqueue(() => ApplyConfiguredColorScheme(persist: false));
        }

        private async void MainWindow_Closing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
        {
            bool hasCancellableWork = _isUpdatingWinget || _storageAnalysisCancellation != null;
            bool hasExternalWork = _isUninstalling || _isDeletingStorage;
            if (!hasCancellableWork && !hasExternalWork)
            {
                _startupCancellation.Cancel();
                return;
            }

            args.Cancel = true;
            if (_closePromptOpen) return;
            _closePromptOpen = true;
            try
            {
                bool en = Localization.CurrentLanguage == "en";
                var dialog = new ContentDialog
                {
                    XamlRoot = RootGrid.XamlRoot,
                    Title = en ? "An operation is still running" : "Vorgang läuft noch",
                    Content = en
                        ? "WinVora is updating, analyzing, cleaning, or waiting for an uninstaller. Closing cancels WinVora tasks; manufacturer uninstallers may remain open."
                        : "WinVora aktualisiert, analysiert, bereinigt oder wartet auf einen Deinstaller. Beim Schließen werden WinVora-Aufgaben abgebrochen; Hersteller-Deinstaller können geöffnet bleiben.",
                    PrimaryButtonText = en ? "Cancel and close" : "Abbrechen und schließen",
                    CloseButtonText = en ? "Keep running" : "Weiterlaufen lassen",
                    DefaultButton = ContentDialogButton.Close
                };
                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    if (_isUpdatingWinget)
                        CancelUpdate_Click(this, new RoutedEventArgs());
                    _storageAnalysisCancellation?.Cancel();
                    _storageAnalysisCancellation?.Dispose();
                    _storageAnalysisCancellation = null;
                    _startupCancellation.Cancel();
                    Logger.Log("Laufende Vorgänge wurden beim Schließen von WinVora abgebrochen.");
                    _isUpdatingWinget = false;
                    _isUninstalling = false;
                    _isDeletingStorage = false;
                    Close();
                }
            }
            finally
            {
                _closePromptOpen = false;
            }
        }

        private void RestoreWindowPlacement()
        {
            try
            {
                var point = new Windows.Graphics.PointInt32(_settings.WindowX ?? 100, _settings.WindowY ?? 100);
                var display = Microsoft.UI.Windowing.DisplayArea.GetFromPoint(
                    point, Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
                var work = display.WorkArea;
                int desiredWidth = Math.Min(_settings.WindowWidth, work.Width);
                int desiredHeight = Math.Min(_settings.WindowHeight, work.Height);
                int desiredX = Math.Clamp(_settings.WindowX ?? work.X + 60, work.X, work.X + work.Width - desiredWidth);
                int desiredY = Math.Clamp(_settings.WindowY ?? work.Y + 60, work.Y, work.Y + work.Height - desiredHeight);
                _postStartupWindowRect = new Windows.Graphics.RectInt32(desiredX, desiredY, desiredWidth, desiredHeight);

                int width = Math.Min(960, work.Width);
                int height = Math.Min(600, work.Height);
                int x = work.X + (work.Width - width) / 2;
                int y = work.Y + (work.Height - height) / 2;
                AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, width, height));
            }
            catch (Exception ex)
            {
                Logger.LogError("Fensterposition konnte nicht wiederhergestellt werden", ex);
            }
        }

        private void SaveWindowPlacement()
        {
            try
            {
                var position = AppWindow.Position;
                var size = AppWindow.Size;
                _settings.WindowX = position.X;
                _settings.WindowY = position.Y;
                _settings.WindowWidth = size.Width;
                _settings.WindowHeight = size.Height;
                _settings.Save();
            }
            catch (Exception ex)
            {
                Logger.LogError("Fensterposition konnte nicht gespeichert werden", ex);
            }
        }

        private void SetupKeyboardShortcuts()
        {
            var refresh = new KeyboardAccelerator
            {
                Key = Windows.System.VirtualKey.R,
                Modifiers = Windows.System.VirtualKeyModifiers.Control
            };
            refresh.Invoked += async (_, args) =>
            {
                args.Handled = true;
                if (_currentPageKey == "Updates") await LoadWinget(forceRefresh: true);
                else if (_currentPageKey == "Storage") await LoadStorage();
                else if (_currentPageKey == "Uninstall") await LoadInstalledPrograms();
                else if (_currentPageKey is "System" or "Übersicht")
                {
                    _cachedSnapshot = null;
                    await LoadSystemSnapshotIfNeededAsync(
                        Localization.T("Common.LoadingSystemInfo"), "Fehler beim Aktualisieren");
                }
            };

            var search = new KeyboardAccelerator
            {
                Key = Windows.System.VirtualKey.F,
                Modifiers = Windows.System.VirtualKeyModifiers.Control
            };
            search.Invoked += (_, args) =>
            {
                if (_currentPageKey == "Updates") WingetSearchBox.Focus(FocusState.Keyboard);
                else if (_currentPageKey == "Uninstall") UninstallSearchBox.Focus(FocusState.Keyboard);
                else return;
                args.Handled = true;
            };

            var commandPalette = new KeyboardAccelerator
            {
                Key = Windows.System.VirtualKey.K,
                Modifiers = Windows.System.VirtualKeyModifiers.Control
            };
            commandPalette.Invoked += async (_, args) =>
            {
                args.Handled = true;
                await ShowCommandPaletteAsync();
            };

            RootGrid.KeyboardAccelerators.Add(refresh);
            RootGrid.KeyboardAccelerators.Add(search);
            RootGrid.KeyboardAccelerators.Add(commandPalette);
        }

        // Setzt alle übersetzbaren Texte (Sidebar, Dashboard, Schnellzugriff)
        // gemäß der aktuell gewählten Sprache. Wird beim Start UND jedes Mal
        // aufgerufen, wenn die Sprache in den Einstellungen geändert wird.
        private void ApplyLanguage()
        {
            // Sidebar-Navigation
            LblNavDashboard.Text = Localization.T("Nav.Dashboard");
            LblNavSystem.Text = Localization.T("Nav.System");
            LblNavUpdates.Text = Localization.CurrentLanguage == "en" ? "Updates" : "Updates";
            LblNavFiles.Text = Localization.T("Nav.Files");
            LblNavUninstall.Text = Localization.T("Nav.Uninstall");
            LblNavHistory.Text = Localization.CurrentLanguage == "en" ? "History" : "Verlauf";
            LblNavAutostart.Text = "Autostart";
            LblNavChanges.Text = Localization.CurrentLanguage == "en" ? "Changes" : "Veränderungen";
            LblNavSettings.Text = Localization.T("Nav.Settings");
            LblNavContact.Text = Localization.T("Nav.Contact");
            LblNavChangelogHint.Text = Localization.T("Nav.ChangelogHint");
            StartupStatusText.Text = Localization.T("Common.Loading");

            // Statuskarten
            LblStatCpu.Text = Localization.T("Stat.Cpu");
            LblStatCpuSub.Text = Localization.T("Stat.CpuLabel");
            LblStatRam.Text = Localization.T("Stat.Ram");
            LblStatGpu.Text = Localization.T("Stat.Gpu");
            LblStatGpuSub.Text = Localization.T("Stat.GpuLabel");
            LblStatSecurity.Text = Localization.T("Stat.Security");
            LblStatSecuritySub.Text = Localization.T("Stat.SecurityLabel");
            LblStatUpdates.Text = Localization.T("Stat.Updates");
            LblStatUpdatesSub.Text = Localization.T("Stat.UpdatesLabel");

            // Live-Dashboard
            LblDashDisk.Text = Localization.T("Dash.Disk");
            LblDashPrograms.Text = Localization.T("Dash.Programs");
            LblDashCleanup.Text = Localization.T("Dash.Cleanup");
            LblDashUpdatesAvailable.Text = Localization.T("Dash.UpdatesAvailable");
            LblDashRam.Text = Localization.T("Dash.Ram");
            LblDashStatus.Text = Localization.T("Dash.Status");
            DashOverallBadgeText.Text = Localization.CurrentLanguage == "en" ? "Everything looks good" : "Alles in Ordnung";

            // Verlaufsdiagramme + Aktivitätsverlauf
            HistoryCard.Text = Localization.CurrentLanguage == "en" ? "Usage over the last few minutes" : "Auslastung der letzten Minuten";
            LblHistoryCpu.Text = Localization.T("Stat.Cpu");
            LblHistoryRam.Text = Localization.T("Stat.Ram");
            LblHistoryGpu.Text = Localization.T("Stat.Gpu");

            // Systeminfo: Action-Bar + Abschnitts-Überschriften
            RefreshSystemInfoButton.Content = new FontIcon { Glyph = "\uE72C" };
            ToolTipService.SetToolTip(RefreshSystemInfoButton, Localization.T("System.Refresh"));
            ExpandAllSystemButton.Content = Localization.T("System.ExpandAll");
            CollapseAllSystemButton.Content = Localization.T("System.CollapseAll");
            DeviceExpander.Header = Localization.T("System.Device");
            OsExpander.Header = Localization.T("System.Os");
            CpuExpander.Header = Localization.T("System.Cpu");
            RamExpander.Header = Localization.T("System.Ram");
            BoardExpander.Header = Localization.T("System.Board");
            SecurityExpander.Header = Localization.T("System.Security");
            GpuExpander.Header = Localization.T("System.Gpu");
            DrivesExpander.Header = Localization.T("System.Drives");
            NetworkExpander.Header = Localization.T("System.Network");
            BatteryExpander.Header = Localization.T("System.Battery");

            SysCardDevice.Header = Localization.T("System.Card.Device");
            SysCardOs.Header = Localization.T("System.Card.Os");
            SysCardCpu.Header = Localization.T("System.Card.Cpu");
            SysCardRam.Header = Localization.T("System.Card.Ram");
            SysCardBoard.Header = Localization.T("System.Card.Board");
            SysCardSecurity.Header = Localization.T("System.Card.Security");
            SysCardGpu.Header = Localization.T("System.Card.Gpu");
            SysCardDrives.Header = Localization.T("System.Card.Drives");
            SysCardNetwork.Header = Localization.T("System.Card.Network");
            SysCardBattery.Header = Localization.T("System.Card.Battery");

            // Alle 26 Feldbezeichnungen auf der Systeminfo-Seite in einem
            // Rutsch übersetzen. Direkter Feldzugriff statt FindName() - so
            // schlägt es beim Kompilieren fehl, falls ein Name nicht mehr
            // stimmt, statt zur Laufzeit still zu nichts zu tun.
            var sysLabels = new[]
            {
                SysLbl01, SysLbl02, SysLbl03, SysLbl04, SysLbl05, SysLbl06, SysLbl07,
                SysLbl08, SysLbl09, SysLbl10, SysLbl11, SysLbl12, SysLbl13, SysLbl14,
                SysLbl15, SysLbl16, SysLbl17, SysLbl18, SysLbl19, SysLbl20, SysLbl21,
                SysLbl22, SysLbl23, SysLbl24, SysLbl25, SysLbl26
            };
            for (int i = 0; i < sysLabels.Length && i < Localization.SystemFieldLabels.Length; i++)
            {
                var (de, enText) = Localization.SystemFieldLabels[i];
                sysLabels[i].Text = Localization.CurrentLanguage == "en" ? enText : de;
            }

            // Winget: Action-Bar
            WingetSearchBox.PlaceholderText = Localization.T("Winget.SearchPlaceholder");
            ToolTipService.SetToolTip(RefreshButton, Localization.T("Common.Refresh"));
            StartUpdateButton.Content = Localization.T("Winget.StartUpdate");
            UpdateUpdateChannelUi();
            ToolTipService.SetToolTip(UpdateChannelButton,
                Localization.CurrentLanguage == "en"
                    ? "Change the update channel in Settings"
                    : "Updatekanal in den Einstellungen ändern");

            // Storage: Action-Bar
            StorageRefreshButton.Content = new FontIcon { Glyph = "\uE72C" };
            ToolTipService.SetToolTip(StorageRefreshButton, Localization.T("Common.Refresh"));
            StorageDeleteSelectedButton.Content = Localization.T("Storage.DeleteSelected");

            // Deinstaller: Action-Bar
            UninstallSearchBox.PlaceholderText = Localization.T("Uninstall.SearchPlaceholder");
            UninstallRefreshButton.Content = new FontIcon { Glyph = "\uE72C" };
            UninstallExportButton.Content = Localization.CurrentLanguage == "en" ? "Export list" : "Liste exportieren";
            UninstallSelectedButton.Content = Localization.CurrentLanguage == "en" ? "Uninstall selected" : "Ausgewählte deinstallieren";
            ToolTipService.SetToolTip(UninstallRefreshButton, Localization.T("Common.Refresh"));
            ToolTipService.SetToolTip(WingetSelectAllButton,
                Localization.CurrentLanguage == "en" ? "Select or deselect all visible updates" : "Alle sichtbaren Updates aus- oder abwählen");
            UpdateWingetSelectAllAppearance();
            ToolTipService.SetToolTip(StorageSelectAllButton,
                Localization.CurrentLanguage == "en" ? "Select all cleanup categories" : "Alle Bereinigungskategorien auswählen");
            ToolTipService.SetToolTip(StorageDeleteSelectedButton,
                Localization.CurrentLanguage == "en" ? "Delete selected files" : "Ausgewählte Dateien löschen");

            // Große Seiten-Überschrift neu setzen, falls schon eine Seite aktiv ist
            if (!string.IsNullOrEmpty(_currentPageKey))
                PageTitle.Text = GetPageDisplayTitle(_currentPageKey);

            // BUGFIX: Diese Werte werden mit dynamischem Text befüllt (nicht nur
            // die Labels daneben) - ohne diesen Refresh blieben sie beim
            // Sprachwechsel in der ursprünglichen Sprache stehen, obwohl die
            // Beschriftungen daneben schon übersetzt waren.
            var firstDrive = _cachedSnapshot?.Drives?.FirstOrDefault();
            DashDiskText.Text = firstDrive != null
                ? Localization.CurrentLanguage == "en"
                    ? $"{firstDrive.FreeSpace} free of {firstDrive.TotalSize}"
                    : $"{firstDrive.FreeSpace} frei von {firstDrive.TotalSize}"
                : Localization.T("Dash.NotAvailable");

            DashLastCleanupText.Text = FormatLastCleanup(_settings.LastCleanupUtc);

            UpdateDashboardStatusSummary();
        }

        // Baut alle Seiten, die schon Daten geladen haben, mit der neuen
        // Sprache neu auf - vorher blieben z.B. Systeminfo-Werte, Winget-Liste,
        // Storage-Kategorien und Deinstaller-Karten in der alten Sprache
        // stehen, bis man die Seite manuell neu geladen hat.
        private void RefreshLoadedPagesForLanguageChange()
        {
            if (_cachedSnapshot != null)
            {
                ApplySnapshot(_cachedSnapshot);
            }

            if (_cachedPackages != null)
            {
                RenderWingetPackages(_cachedPackages);
            }

            if (StoragePanel.Children.Count > 0)
            {
                _ = LoadStorage();
            }

            if (_installedPrograms.Count > 0)
            {
                _ = LoadInstalledPrograms();
            }
        }

        // Startet die Glas-Balken-Animation erst, wenn der Startbildschirm
        // wirklich im sichtbaren Baum geladen ist - vorher (z.B. direkt im
        // Konstruktor) läuft die Storyboard-Animation praktisch ins Leere,
        // weil das Element noch nicht "live" ist.
        private void StartupOverlay_Loaded(object sender, RoutedEventArgs e)
        {
            // Erst ab dem wirklich gerenderten Overlay messen. Die vorherige
            // Messung begann bereits beim Erzeugen des Fensters und ein Teil der
            // Mindestzeit verstrich dadurch unsichtbar während der XAML-Initialisierung.
            _startupOverlayLifetime.Restart();
            if (_settings.AnimationMode == "Off") return;
            StartStartupGlassBarAnimation();
        }

        // Ruhiger Hover-Zustand ohne zusätzlichen Außenrahmen oder Schatten.
        // Ein Akzentrahmen über einem bereits abgerundeten, halbtransparenten
        // Rand erzeugte bei Windows-Skalierung sichtbare Pixelmuster an den
        // Ecken. Deshalb ändert Hover nur noch dezent die Flächenfarbe.
        private void AttachCardHoverEffect(Border card)
        {
            var originalBackground = card.Background;
            var hoverBackground = (SolidColorBrush)RootGrid.Resources["AppOverlay22"];

            card.PointerEntered += (_, __) => card.Background = hoverBackground;
            card.PointerExited += (_, __) => card.Background = originalBackground;
        }

        private void SetupOverviewCardHoverEffects()
        {
            foreach (var card in new[]
            {
                StatCardCpu, StatCardRam, StatCardGpu, StatCardSecurity, StatCardUpdates,
                DashCardDisk, DashCardTemp, DashCardPrograms,
                DashCardCleanup, DashCardUpdatesDetail, DashCardRam, DashCardStatus
            })
            {
                AttachCardHoverEffect(card);
            }

            foreach (var card in new[] { StatCardUpdates, DashCardDisk, DashCardPrograms, DashCardCleanup })
            {
                if (card.Child is StackPanel content)
                {
                    content.MinHeight = 108;
                    content.Children.Add(new TextBlock
                    {
                        Text = Localization.CurrentLanguage == "en" ? "Open  →" : "Öffnen  →",
                        FontSize = 11,
                        Foreground = (SolidColorBrush)RootGrid.Resources["AppAccentBrushLight"],
                        Margin = new Thickness(0, 3, 0, 0)
                    });
                }
            }
        }

        private void BuildDashboardHierarchy()
        {
            // Wichtigste Informationen zuerst: Gesamtstatus, Updates,
            // Sicherheit und Speicher. CPU/RAM/GPU bilden darunter einen
            // klar getrennten Systemleistungsbereich.
            LiveDashboardGrid.Children.Remove(DashCardStatus);
            LiveDashboardGrid.Children.Remove(DashCardDisk);
            DashboardStatusGrid.Children.Remove(StatCardUpdates);
            DashboardStatusGrid.Children.Remove(StatCardSecurity);
            while (DashboardStatusGrid.ColumnDefinitions.Count > 3)
                DashboardStatusGrid.ColumnDefinitions.RemoveAt(DashboardStatusGrid.ColumnDefinitions.Count - 1);
            OverviewPanel.Children.Remove(LiveDashboardCard);
            OverviewPanel.Children.Remove(DashboardStatusGrid);

            _dashboardPrimaryGrid = new Grid { ColumnSpacing = 16, RowSpacing = 16 };
            for (int i = 0; i < 3; i++)
                _dashboardPrimaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _dashboardPrimaryGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _dashboardPrimaryGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _dashboardPrimaryGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _dashboardPrimaryGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid.SetRow(DashCardStatus, 0); Grid.SetColumn(DashCardStatus, 0); Grid.SetColumnSpan(DashCardStatus, 3);
            Grid.SetRow(StatCardUpdates, 1); Grid.SetColumn(StatCardUpdates, 0); Grid.SetColumnSpan(StatCardUpdates, 1);
            Grid.SetRow(StatCardSecurity, 1); Grid.SetColumn(StatCardSecurity, 1); Grid.SetColumnSpan(StatCardSecurity, 1);
            Grid.SetRow(DashCardDisk, 1); Grid.SetColumn(DashCardDisk, 2); Grid.SetColumnSpan(DashCardDisk, 1);
            _dashboardPrimaryGrid.Children.Add(DashCardStatus);
            _dashboardPrimaryGrid.Children.Add(StatCardUpdates);
            _dashboardPrimaryGrid.Children.Add(StatCardSecurity);
            _dashboardPrimaryGrid.Children.Add(DashCardDisk);

            var systemSection = new StackPanel { Spacing = 12 };
            var performanceHeading = new StackPanel { Spacing = 2 };
            performanceHeading.Children.Add(new TextBlock
            {
                Text = Localization.CurrentLanguage == "en" ? "System performance" : "Systemleistung",
                FontSize = 17,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (SolidColorBrush)RootGrid.Resources["AppForegroundBrush"]
            });
            performanceHeading.Children.Add(new TextBlock
            {
                Text = Localization.CurrentLanguage == "en"
                    ? "Live values for processor, memory and graphics"
                    : "Live-Werte für Prozessor, Arbeitsspeicher und Grafik",
                FontSize = 12,
                Foreground = (SolidColorBrush)RootGrid.Resources["AppMutedForegroundBrush"]
            });
            systemSection.Children.Add(performanceHeading);
            systemSection.Children.Add(DashboardStatusGrid);
            OverviewPanel.Spacing = 18;
            OverviewPanel.Children.Insert(0, _dashboardPrimaryGrid);
            OverviewPanel.Children.Insert(1, systemSection);
            ApplyDashboardCustomizationLayout();
        }

        private void ApplyDashboardCustomizationLayout()
        {
            if (_dashboardPrimaryGrid == null) return;
            bool Hidden(string key) => _settings.HiddenDashboardCards.Contains(key, StringComparer.OrdinalIgnoreCase);
            var primary = new[]
            {
                (Key: "Updates", Card: (FrameworkElement)StatCardUpdates),
                (Key: "Security", Card: (FrameworkElement)StatCardSecurity),
                (Key: "Storage", Card: (FrameworkElement)DashCardDisk)
            };
            var performance = new[]
            {
                (Key: "Cpu", Card: (FrameworkElement)StatCardCpu),
                (Key: "Ram", Card: (FrameworkElement)StatCardRam),
                (Key: "Gpu", Card: (FrameworkElement)StatCardGpu)
            };
            primary = primary.OrderBy(item => _settings.DashboardCardOrder.FindIndex(key => key.Equals(item.Key, StringComparison.OrdinalIgnoreCase))).ToArray();
            performance = performance.OrderBy(item => _settings.DashboardCardOrder.FindIndex(key => key.Equals(item.Key, StringComparison.OrdinalIgnoreCase))).ToArray();
            int maxColumns = _narrowLayoutState == true ? 2 : 3;

            void Layout(Grid grid, (string Key, FrameworkElement Card)[] cards, int firstRow)
            {
                var visible = cards.Where(item => !Hidden(item.Key)).ToList();
                foreach (var item in cards)
                    item.Card.Visibility = Hidden(item.Key) ? Visibility.Collapsed : Visibility.Visible;
                int columns = Math.Max(1, Math.Min(maxColumns, visible.Count));
                for (int index = 0; index < grid.ColumnDefinitions.Count; index++)
                    grid.ColumnDefinitions[index].Width = index < columns
                        ? new GridLength(1, GridUnitType.Star)
                        : new GridLength(0);
                for (int index = 0; index < visible.Count; index++)
                {
                    Grid.SetRow(visible[index].Card, firstRow + index / columns);
                    Grid.SetColumn(visible[index].Card, index % columns);
                    Grid.SetColumnSpan(visible[index].Card, 1);
                }
            }

            int primaryColumns = Math.Max(1, Math.Min(maxColumns, primary.Count(item => !Hidden(item.Key))));
            Grid.SetRow(DashCardStatus, 0);
            Grid.SetColumn(DashCardStatus, 0);
            Grid.SetColumnSpan(DashCardStatus, primaryColumns);
            Layout(_dashboardPrimaryGrid, primary, 1);
            Layout(DashboardStatusGrid, performance, 0);
        }

        private void NormalizeCardMetrics()
        {
            // Eine gemeinsame visuelle Grundlinie für alle kompakten
            // Dashboard-Karten: gleiche Höhe, Innenabstände und Ecken.
            foreach (var dashboardCard in new[]
            {
                StatCardUpdates, StatCardSecurity, DashCardDisk,
                StatCardCpu, StatCardRam, StatCardGpu
            })
            {
                dashboardCard.MinHeight = UiMetrics.DashboardCardHeight;
                dashboardCard.Padding = new Thickness(UiMetrics.CardPadding);
                dashboardCard.CornerRadius = new CornerRadius(UiMetrics.CardRadius);
                dashboardCard.BorderThickness = new Thickness(0);
                dashboardCard.Background = (SolidColorBrush)RootGrid.Resources["AppCardSurfaceBrush"];
            }
            DashCardStatus.BorderThickness = new Thickness(0);
            DashChangesCard.BorderThickness = new Thickness(0);
            foreach (var secondaryCard in new[]
            {
                DashCardTemp, DashCardPrograms, DashCardCleanup,
                DashCardUpdatesDetail, DashCardRam
            })
                secondaryCard.BorderThickness = new Thickness(0);

            // Systeminformationen bleiben zweispaltig, erhalten aber überall
            // dieselbe Kartenhöhe und denselben Innenabstand.
            foreach (var systemCard in new[]
            {
                SysCardDevice, SysCardOs, SysCardCpu, SysCardRam, SysCardBoard,
                SysCardSecurity, SysCardGpu, SysCardDrives, SysCardNetwork, SysCardBattery
            })
            {
                systemCard.MinHeight = 140;
                systemCard.Padding = new Thickness(UiMetrics.CardPadding);
                systemCard.CornerRadius = new CornerRadius(UiMetrics.CardRadius);
                systemCard.BorderThickness = new Thickness(0);
                systemCard.Background = (SolidColorBrush)RootGrid.Resources["AppCardSurfaceBrush"];
                NormalizeSystemInfoValues(systemCard);
            }
        }

        private static void NormalizeSystemInfoValues(DependencyObject root)
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int index = 0; index < count; index++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, index);
                if (child is TextBlock text && Grid.GetColumn(text) == 1)
                {
                    text.MaxWidth = 520;
                    text.MaxLines = 2;
                    text.TextTrimming = TextTrimming.CharacterEllipsis;
                    text.HorizontalAlignment = HorizontalAlignment.Right;
                    ToolTipService.SetToolTip(text, text.Text);
                    text.RegisterPropertyChangedCallback(TextBlock.TextProperty,
                        (_, __) => ToolTipService.SetToolTip(text, text.Text));
                }
                NormalizeSystemInfoValues(child);
            }
        }

        private static void RemoveRoundedDecorativeBorders(DependencyObject root)
        {
            int childCount = VisualTreeHelper.GetChildrenCount(root);
            for (int index = 0; index < childCount; index++)
            {
                var child = VisualTreeHelper.GetChild(root, index);
                if (child is Border border && border.CornerRadius != new CornerRadius(0))
                    border.BorderThickness = new Thickness(0);
                RemoveRoundedDecorativeBorders(child);
            }
        }

        // Erzeugt und startet mehrere Liquid-Glass-Bänder, die über den ganzen
        // Startbildschirm verteilt in unterschiedlichen Größen, auf leicht
        // unterschiedlichen Diagonal-Wegen und zeitlich versetzt durchlaufen.
        private void StartStartupGlassBarAnimation()
        {
            // Hex-Grid-Hintergrund: ein Wabenmuster aus dünnen Sechsecken.
            // Die Umriss-Linien selbst leuchten in einer Lila-Welle, die von
            // links nach rechts durchs ganze Netz läuft (siehe AnimateHexGlow).
            // Keine zusätzlichen Lichtstreifen mehr darüber - die wirkten
            // eher störend als stimmig.
            BuildHexGridBackground();
        }

        // Drei Farbtöne innerhalb der App-Akzentfamilie (Violett-Blau), damit
        // der Hintergrund zur restlichen Oberfläche passt statt wie ein
        // beliebiger Regenbogen zu wirken.
        private static readonly Windows.UI.Color AccentColorPrimary = Windows.UI.Color.FromArgb(0xFF, 0x6C, 0x5C, 0xE7);
        private static readonly Windows.UI.Color AccentColorLight = Windows.UI.Color.FromArgb(0xFF, 0x8B, 0x7C, 0xF6);
        private static readonly Windows.UI.Color AccentColorCool = Windows.UI.Color.FromArgb(0xFF, 0x4F, 0x8C, 0xF0);

        // Baut ein Wabenraster aus dünnen Sechseck-Umrissen, das den ganzen
        // Startbildschirm abdeckt. Jede Zelle bekommt ihre eigene, leicht
        // versetzte Puls-Animation (Opacity rauf/runter), damit das Muster
        // insgesamt lebendig wirkt statt wie ein statisches Bild.
        private void BuildHexGridBackground()
        {
            _startupHexStoryboard?.Stop();
            StartupGlassBandsHost.Children.Clear();

            // Größere Zellen reduzieren die Anzahl der XAML-Polygone deutlich
            // und senken damit CPU-/GPU-Last des kurzen Startbildschirms.
            const double hexSize = 48;
            double hexWidth = hexSize * 2;
            double hexHeight = Math.Sqrt(3) * hexSize;
            double colSpacing = hexWidth * 0.75;
            double rowSpacing = hexHeight;

            // Nur die tatsächlich sichtbare Fläche plus eine Zelle Reserve
            // aufbauen. Das frühere feste 74x40-Raster erzeugte fast 3.000
            // XAML-Elemente und machte das Verschieben des Fensters unnötig
            // teuer.
            double overlayWidth = Math.Max(StartupOverlay.ActualWidth, this.Bounds.Width);
            double overlayHeight = Math.Max(StartupOverlay.ActualHeight, this.Bounds.Height);
            try
            {
                // Das Raster von Anfang an für die komplette Arbeitsfläche bauen.
                // Beim Vergrößern des Fensters werden dadurch bereits vorhandene,
                // weiter animierte Waben sichtbar, ohne die Animation neu zu starten.
                var display = Microsoft.UI.Windowing.DisplayArea.GetFromPoint(
                    AppWindow.Position,
                    Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
                overlayWidth = Math.Max(overlayWidth, display.WorkArea.Width);
                overlayHeight = Math.Max(overlayHeight, display.WorkArea.Height);
            }
            catch
            {
                // Die aktuelle Fenstergröße bleibt ein sicherer Fallback.
            }
            if (overlayWidth <= 0) overlayWidth = 1200;
            if (overlayHeight <= 0) overlayHeight = 720;
            // Kein BitmapCache: dessen beim Start festgelegte Fläche wuchs beim
            // Live-Resize nicht mit und ließ die Waben scheinbar stehen bleiben.
            StartupGlassBandsHost.CacheMode = null;

            int cols = Math.Max(1, (int)Math.Ceiling(overlayWidth / colSpacing) + 2);
            int rows = Math.Max(1, (int)Math.Ceiling(overlayHeight / rowSpacing) + 2);
            double gridWidth = (cols - 1) * colSpacing + hexWidth;
            double gridHeight = (rows - 1) * rowSpacing + rowSpacing + hexHeight / 2;

            // BUGFIX: Vorher war der Start-Offset fest verdrahtet (-120,-120),
            // unabhängig von der tatsächlichen Fenstergröße - dadurch saß das
            // Logo nicht wirklich in der Mitte des Musters, sondern mehr am
            // linken Rand. Jetzt wird das Raster anhand der ECHTEN Größe des
            // Startbildschirms (StartupOverlay.ActualWidth/Height) mittig
            // ausgerichtet: Rastermitte = Fenstermitte.
            // BUGFIX: StartupOverlay.ActualWidth/Height spiegelte beim Loaded-
            // Event offenbar noch nicht die echte Fenstergröße wider (Grid mit
            // ColumnSpan über die "*"-Spalte war zu diesem Zeitpunkt evtl. noch
            // nicht final aufgelöst) - das Raster saß dadurch weit außerhalb
            // der Mitte, nur ein Randstreifen war sichtbar. this.Bounds (die
            // tatsächliche Fenstergröße) ist an dieser Stelle zuverlässiger.
            double startOffsetX = (overlayWidth - gridWidth) / 2;
            double startOffsetY = (overlayHeight - gridHeight) / 2;

            byte baseStrokeAlpha = _isDarkTheme ? (byte)0x18 : (byte)0x48;
            byte baseFillAlpha = _isDarkTheme ? (byte)0x08 : (byte)0x12;
            byte haloAlpha = _isDarkTheme ? (byte)0x36 : (byte)0x48;
            var sharedStrokeBrush = new SolidColorBrush(
                Windows.UI.Color.FromArgb(baseStrokeAlpha, AccentColorPrimary.R, AccentColorPrimary.G, AccentColorPrimary.B));
            var sharedFillBrush = new SolidColorBrush(
                Windows.UI.Color.FromArgb(baseFillAlpha, AccentColorPrimary.R, AccentColorPrimary.G, AccentColorPrimary.B));
            var glowStrokeBrush = new SolidColorBrush(
                Windows.UI.Color.FromArgb(0xFF, AccentColorLight.R, AccentColorLight.G, AccentColorLight.B));
            var glowHaloBrush = new SolidColorBrush(
                Windows.UI.Color.FromArgb(haloAlpha, AccentColorPrimary.R, AccentColorPrimary.G, AccentColorPrimary.B));
            var transparentFillBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

            // Mehrere deckungsgleiche Leuchtebenen teilen die Waben in
            // unregelmäßige Bereiche. Die Ebenen pulsieren zeitversetzt, sodass
            // einzelne zusammenhängende Fugen wie Lava aufglühen, ohne dass ein
            // sichtbarer Lichtbalken in eine feste Richtung läuft.
            var glowCanvases = Enumerable.Range(0, 4)
                .Select(_ => new Canvas
                {
                    Width = overlayWidth,
                    Height = overlayHeight,
                    Opacity = 0.08,
                    IsHitTestVisible = false
                })
                .ToArray();

            for (int col = 0; col < cols; col++)
            {
                double x = startOffsetX + col * colSpacing;
                double yOffset = (col % 2 == 0) ? 0 : rowSpacing / 2;

                for (int row = 0; row < rows; row++)
                {
                    double y = startOffsetY + row * rowSpacing + yOffset;

                    var hex = CreateHexCell(hexSize, sharedStrokeBrush, sharedFillBrush);
                    Canvas.SetLeft(hex, x);
                    Canvas.SetTop(hex, y);
                    StartupGlassBandsHost.Children.Add(hex);

                    // Breiter, transparenter Schein plus scharfer heller Kern:
                    // Dadurch wirken die Fugen wie violett glühendes Material
                    // zwischen nahezu schwarzen Wabenflächen.
                    var glowHaloHex = CreateHexCell(hexSize, glowHaloBrush, transparentFillBrush, 7.5);
                    Canvas.SetLeft(glowHaloHex, x);
                    Canvas.SetTop(glowHaloHex, y);
                    int glowGroup = Math.Abs((col / 3) * 11 + (row / 2) * 7 + col * row) % glowCanvases.Length;
                    glowCanvases[glowGroup].Children.Add(glowHaloHex);

                    var glowHex = CreateHexCell(hexSize, glowStrokeBrush, transparentFillBrush, 2.2);
                    Canvas.SetLeft(glowHex, x);
                    Canvas.SetTop(glowHex, y);
                    glowCanvases[glowGroup].Children.Add(glowHex);
                }
            }

            foreach (var glowCanvas in glowCanvases)
                StartupGlassBandsHost.Children.Add(glowCanvas);
            StartSharedHexAnimation(glowCanvases);
        }

        // Ein einzelnes Sechseck: dünner Umriss (per SolidColorBrush, wird für
        // den Leucht-Effekt separat animiert), ganz leichte Füllung.
        private Microsoft.UI.Xaml.Shapes.Polygon CreateHexCell(
            double size,
            SolidColorBrush strokeBrush,
            SolidColorBrush fillBrush,
            double strokeThickness = 1.4)
        {
            var points = new PointCollection();
            for (int i = 0; i < 6; i++)
            {
                double angle = Math.PI / 180 * (60 * i);
                points.Add(new Point(size + size * Math.Cos(angle), size + size * Math.Sin(angle)));
            }

            // Dunkler Startzustand: bewusst sehr niedrige Deckkraft, damit die
            // Linien dort, wo die Welle gerade NICHT ist, deutlich dunkel und
            // fast unsichtbar wirken - starker Kontrast zum hellen Leuchten.
            return new Microsoft.UI.Xaml.Shapes.Polygon
            {
                Points = points,
                Width = size * 2,
                Height = size * 2,
                Stroke = strokeBrush,
                StrokeThickness = strokeThickness,
                Fill = fillBrush
            };
        }

        // Lässt mehrere unregelmäßige Wabengruppen zeitversetzt aufglühen.
        // Das ergibt einen organischen Lava-Effekt ohne feste Laufrichtung.
        private void StartSharedHexAnimation(Canvas[] glowCanvases)
        {
            _startupHexStoryboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
            for (int i = 0; i < glowCanvases.Length; i++)
            {
                bool reduced = _settings.AnimationMode == "Reduced";
                double cycleSeconds = reduced ? 9.0 : 6.0;
                double peak = reduced ? 0.34 : 0.78;
                var pulseAnimation = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimationUsingKeyFrames
                {
                    Duration = TimeSpan.FromSeconds(cycleSeconds),
                    BeginTime = TimeSpan.FromSeconds(i * (cycleSeconds / glowCanvases.Length)),
                    RepeatBehavior = Microsoft.UI.Xaml.Media.Animation.RepeatBehavior.Forever,
                    EnableDependentAnimation = true
                };
                pulseAnimation.KeyFrames.Add(new Microsoft.UI.Xaml.Media.Animation.SplineDoubleKeyFrame
                    { KeyTime = TimeSpan.Zero, Value = 0.02 });
                pulseAnimation.KeyFrames.Add(new Microsoft.UI.Xaml.Media.Animation.SplineDoubleKeyFrame
                    { KeyTime = TimeSpan.FromSeconds(0.65), Value = peak });
                pulseAnimation.KeyFrames.Add(new Microsoft.UI.Xaml.Media.Animation.SplineDoubleKeyFrame
                    { KeyTime = TimeSpan.FromSeconds(1.35), Value = 0.02 });
                pulseAnimation.KeyFrames.Add(new Microsoft.UI.Xaml.Media.Animation.DiscreteDoubleKeyFrame
                    { KeyTime = TimeSpan.FromSeconds(cycleSeconds), Value = 0.02 });

                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(pulseAnimation, glowCanvases[i]);
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(pulseAnimation, "Opacity");
                _startupHexStoryboard.Children.Add(pulseAnimation);
            }
            _startupHexStoryboard.Begin();
        }

        private void LogActivity(string iconGlyph, string textDe, string textEn, string result = "Successful")
        {
            _settings.ActivityLog.Insert(0, new ActivityLogEntry
            {
                TimestampUtc = DateTime.UtcNow,
                IconGlyph = iconGlyph,
                TextDe = textDe,
                TextEn = textEn,
                Result = result
            });

            // Genug Historie für Diagnose und Filter behalten, ohne die
            // unbegrenzt wächst.
            while (_settings.ActivityLog.Count > 100)
                _settings.ActivityLog.RemoveAt(_settings.ActivityLog.Count - 1);

            _settings.Save();
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

            if (RootGrid.Resources["AppSuccessBrush"] is SolidColorBrush successBrush)
                successBrush.Color = dark
                    ? Windows.UI.Color.FromArgb(0xFF, 0x4C, 0xD9, 0x73)
                    : Windows.UI.Color.FromArgb(0xFF, 0x1F, 0x8A, 0x45);
            if (RootGrid.Resources["AppWarningBrush"] is SolidColorBrush warningBrush)
                warningBrush.Color = dark
                    ? Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xC1, 0x4D)
                    : Windows.UI.Color.FromArgb(0xFF, 0xA8, 0x68, 0x00);
            if (RootGrid.Resources["AppCardSurfaceBrush"] is SolidColorBrush cardSurface)
                cardSurface.Color = dark
                    ? Windows.UI.Color.FromArgb(0xFF, 0x1C, 0x1B, 0x20)
                    : Windows.UI.Color.FromArgb(0xFF, 0xEC, 0xEA, 0xF2);
            if (RootGrid.Resources["AppDangerSurfaceBrush"] is SolidColorBrush dangerSurface)
                dangerSurface.Color = dark
                    ? Windows.UI.Color.FromArgb(0xFF, 0x3A, 0x23, 0x28)
                    : Windows.UI.Color.FromArgb(0xFF, 0xFC, 0xE8, 0xEA);
            if (RootGrid.Resources["AppSelectionEmptyBrush"] is SolidColorBrush emptySelection)
                emptySelection.Color = dark
                    ? Windows.UI.Color.FromArgb(0xFF, 0x45, 0x45, 0x45)
                    : Windows.UI.Color.FromArgb(0xFF, 0xD0, 0xD0, 0xD0);
            if (Application.Current.Resources["WinVoraSecondaryButtonBrush"] is SolidColorBrush secondaryButton)
                secondaryButton.Color = dark
                    ? Windows.UI.Color.FromArgb(0xFF, 0x30, 0x2B, 0x50)
                    : Windows.UI.Color.FromArgb(0xFF, 0xEE, 0xEA, 0xFB);
            if (Application.Current.Resources["WinVoraToggleOffBrush"] is SolidColorBrush toggleOff)
                toggleOff.Color = dark
                    ? Windows.UI.Color.FromArgb(0xFF, 0x38, 0x38, 0x38)
                    : Windows.UI.Color.FromArgb(0xFF, 0xD8, 0xD8, 0xD8);
            SetApplicationBrush("ToggleSwitchFillOff",
                dark ? Windows.UI.Color.FromArgb(0xFF, 0x38, 0x38, 0x38) : Windows.UI.Color.FromArgb(0xFF, 0xD8, 0xD8, 0xD8));
            SetApplicationBrush("ToggleSwitchFillOffPointerOver",
                dark ? Windows.UI.Color.FromArgb(0xFF, 0x42, 0x42, 0x42) : Windows.UI.Color.FromArgb(0xFF, 0xC8, 0xC8, 0xC8));
            SetApplicationBrush("ToggleSwitchFillOffPressed",
                dark ? Windows.UI.Color.FromArgb(0xFF, 0x30, 0x30, 0x30) : Windows.UI.Color.FromArgb(0xFF, 0xBC, 0xBC, 0xBC));
            SetApplicationBrush("AccentButtonBackgroundDisabled",
                dark ? Windows.UI.Color.FromArgb(0x59, 0x6C, 0x5C, 0xE7) : Windows.UI.Color.FromArgb(0xFF, 0xBF, 0xB8, 0xEC));
            SetApplicationBrush("AccentButtonForegroundDisabled",
                dark ? Windows.UI.Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF) : Windows.UI.Color.FromArgb(0xB0, 0x20, 0x1D, 0x2A));
            if (MainCard.Resources.TryGetValue("ButtonBackgroundDisabled", out var disabledValue) && disabledValue is SolidColorBrush disabledBrush)
                disabledBrush.Color = dark
                    ? Windows.UI.Color.FromArgb(0xFF, 0x24, 0x23, 0x33)
                    : Windows.UI.Color.FromArgb(0xFF, 0xE1, 0xDE, 0xE9);

            if (RootGrid.Resources["AppRootBackgroundBrush"] is SolidColorBrush rootBrush)
                rootBrush.Color = dark
                    ? Microsoft.UI.ColorHelper.FromArgb(0xF0, 0x00, 0x00, 0x00)
                    : Microsoft.UI.ColorHelper.FromArgb(0xF0, 0xFF, 0xFF, 0xFF);

            RootGrid.RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light;
            ApplyStartupOverlayTheme(dark);

            // Bereits geladene dynamische Statusfarben sofort an das neue
            // Farbschema anpassen, ohne eine erneute Systemprüfung abzuwarten.
            if (HealthSecurityText != null)
            {
                HealthSecurityText.Foreground = new SolidColorBrush(_securityHealthState switch
                {
                    SecurityHealthState.Active => GetHealthyStatusColor(),
                    SecurityHealthState.Problem => GetStatusColor("AppWarningBrush"),
                    _ => GetStatusColor("AppNeutralStatusBrush")
                });
                UpdateDashboardStatusSummary();
            }

            ApplyTitleBarColors(dark);
            if (_settingsWindow != null)
                ApplySecondaryWindowTitleBarColors(_settingsWindow, dark);
            if (_changelogWindow != null)
                ApplySecondaryWindowTitleBarColors(_changelogWindow, dark);
            ApplyGlassIntensity(_settings.GlassIntensity);

            if (persist)
            {
                _settings.Save();
            }
        }

        private static void SetApplicationBrush(string key, Windows.UI.Color color)
        {
            if (Application.Current.Resources.TryGetValue(key, out var resource) && resource is SolidColorBrush brush)
                brush.Color = color;
        }

        private void ApplyConfiguredColorScheme(bool persist = true)
        {
            bool dark = _settings.ColorScheme switch
            {
                "Dark" => true,
                "Light" => false,
                _ => Application.Current.RequestedTheme == ApplicationTheme.Dark
            };
            ApplyTheme(dark, persist: false);
            if (persist)
            {
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
        // Zeigt beim allerersten Start eine einmalige Sprachauswahl. Wird nur
        // gezeigt, solange _settings.HasChosenLanguage noch false ist.
        private async Task ShowFirstRunLanguagePromptAsync()
        {
            var languageCombo = new ComboBox
            {
                Width = 220,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            PreventClosedComboBoxWheelChange(languageCombo);
            languageCombo.Items.Add(new ComboBoxItem { Content = "Deutsch", Tag = "de" });
            languageCombo.Items.Add(new ComboBoxItem { Content = "English", Tag = "en" });
            languageCombo.SelectedIndex = 0;

            var panel = new StackPanel { Spacing = 16 };
            panel.Children.Add(new TextBlock
            {
                Text = "In welcher Sprache soll WinVora angezeigt werden?\nIn which language should WinVora be displayed?",
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(languageCombo);

            var dialog = new ContentDialog
            {
                Title = "Sprache wählen / Choose Language",
                Content = panel,
                PrimaryButtonText = "OK",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.Content.XamlRoot
            };

            await dialog.ShowAsync();

            var selectedTag = (languageCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "de";

            _settings.Language = selectedTag;
            _settings.HasChosenLanguage = true;
            _settings.Save();

            Localization.CurrentLanguage = _settings.Language;
            ApplyLanguage();
        }

        private async void MainWindow_Activated(object sender, WindowActivatedEventArgs e)
        {
            if (_initialized) return;
            _initialized = true;

            this.Activated -= MainWindow_Activated;

            await LoadInitialDataAsync();

            // Nach der Startoptimierung ist WinVora häufig schon nach gut einer
            // Sekunde fertig. Der Ladebildschirm bleibt mindestens drei Sekunden
            // sichtbar, damit Logo, Animation und Abschlusszustand nicht nur
            // kurz aufblitzen. Langsame Starts werden dadurch nicht verlängert.
            TimeSpan minimumStartupOverlayTime = TimeSpan.FromSeconds(3);
            TimeSpan remainingOverlayTime = minimumStartupOverlayTime - _startupOverlayLifetime.Elapsed;
            if (remainingOverlayTime > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(remainingOverlayTime, _startupCancellation.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }

            // Responsive Umbauten während des verdeckten Startladens können
            // die Scroll-Ankerposition verschieben. Vor dem Einblenden immer
            // garantiert am echten Seitenanfang starten.
            RootGrid.UpdateLayout();
            MainContentScrollViewer.ChangeView(null, 0, null, disableAnimation: true);
            HideStartupOverlay();
            _ = LoadDeferredProgramDataAsync();
            _ = LoadWingetAtStartupInBackgroundAsync();

            // BUGFIX: ContentDialog.ShowAsync() direkt beim allerersten
            // Activated-Event aufzurufen (bevor das Fenster überhaupt einmal
            // fertig gerendert wurde) blockiert auf manchen Systemen komplett -
            // der Bildschirm blieb schwarz mit Warte-Cursor, ohne Fehler im Log.
            // Jetzt läuft die Sprachauswahl erst, NACHDEM das Fenster normal
            // geladen und gezeichnet wurde.
            if (!_settings.HasChosenLanguage)
            {
                await ShowFirstRunLanguagePromptAsync();
            }

            // Läuft bewusst NICHT awaited hier - der Start soll dadurch nicht
            // verzögert werden. Läuft im Hintergrund und zeigt bei Erfolg nur
            // still den kleinen Badge am Einstellungen-Button an.
            _ = CheckForUpdateInBackgroundAsync();
        }

        // Stille Update-Prüfung im Hintergrund (kein Dialog, keine Störung) -
        // zeigt bei Erfolg nur den kleinen roten Badge am Einstellungen-Button.
        private async Task CheckForUpdateInBackgroundAsync()
        {
            try
            {
                var update = await UpdateService.CheckForUpdateAsync(
                    CurrentVersion,
                    _settings.UpdateChannel == "Beta");
                if (update != null)
                {
                    _pendingUpdateInfo = update;
                    UpdateAvailableBadge.Visibility = Visibility.Visible;
                    Logger.Log($"Hintergrund-Update-Check: Version {update.Version} verfügbar.");
                }
            }
            catch (Exception ex)
            {
                // Bewusst nur geloggt, kein Fehlerdialog - das ist eine stille
                // Hintergrundprüfung, die den Nutzer nicht stören soll.
                Logger.LogError("CheckForUpdateInBackgroundAsync", ex);
            }
        }

        // BUGFIX: Der Ladebildschirm wurde vorher sofort wieder ausgeblendet,
        // ohne dass irgendetwas geladen wurde - man landete auf einer leeren
        // Übersicht ("--%"), die sich erst danach sichtbar aufgebaut hat.
        // Jetzt bleibt der Ladebildschirm sichtbar, bis Systeminfos und
        // Winget-Status wirklich fertig geladen sind.
        private async Task LoadInitialDataAsync()
        {
            // Ganz früh im Hintergrund anstoßen (parallel zum restlichen
            // Laden), damit der CPU-Performance-Counter genug Vorlaufzeit hat
            // und die erste echte Live-Anzeige nicht falsch/niedrig ist.
            _ = Task.Run(() => SystemInfoProvider.WarmUpCpuCounter());

            // LibreHardwareMonitor ebenfalls früh öffnen - das erste Öffnen
            // (Treiber laden, Hardware erkennen) kann spürbar dauern.
            _ = Task.Run(() => HardwareMonitorService.WarmUp());

            StartupStatusText.Text = Localization.CurrentLanguage == "en" ? "Preparing interface..." : "Oberfläche wird vorbereitet...";
            StartupProgressBar.Value = 1;
            StartupProgressText.Text = Localization.CurrentLanguage == "en" ? "Step 1 of 4" : "Schritt 1 von 4";
            StartLiveUsageTimer();

            // Letzte bekannte Werte sofort einsetzen. Die frische Abfrage
            // ersetzt sie anschließend im Hintergrund. Dadurch startet das
            // Dashboard auch offline nicht mehr mit leeren Platzhaltern.
            if (StartupSnapshotCache.TryLoad(out var cachedStartupSnapshot, out var cacheTimeUtc))
            {
                _cachedSnapshot = cachedStartupSnapshot;
                ApplySnapshot(cachedStartupSnapshot);
                Logger.Log($"Startcache angewendet (Stand {cacheTimeUtc.ToLocalTime():G}).");
            }

            // Konfigurierte Startseite anzeigen (Standard: Übersicht).
            switch (_settings.StartupPage)
            {
                case "System":
                    SetPage("System");
                    break;

                case "Updates":
                    SetPage("Updates");
                    break;

                case "Storage":
                    SetPage("Storage");
                    break;

                default:
                    SetPage("Übersicht");
                    break;
            }
            // Cache und Grundlayout reichen zum Anzeigen des Hauptfensters.
            // Frische Systemwerte dürfen den sichtbaren Start nicht blockieren.
            _initialBackgroundRefresh = LoadDeferredStartupDataAsync();
        }

        private async Task LoadDeferredStartupDataAsync()
        {
            // Alle benötigten Startdaten parallel hinter dem Ladebildschirm
            // abrufen. Erst wenn diese Aufgaben fertig sind, blendet der
            // Aufrufer das vollständige Hauptfenster ein.
            var timer = Stopwatch.StartNew();
            var cancellationToken = _startupCancellation.Token;
            void SetPhase(string german, string english, int step)
            {
                if (cancellationToken.IsCancellationRequested) return;
                StartupStatusText.Text = Localization.CurrentLanguage == "en"
                    ? $"Step {step} of 4 · {english}"
                    : $"Schritt {step} von 4 · {german}";
                StartupProgressBar.Value = step;
                StartupProgressText.Text = Localization.CurrentLanguage == "en"
                    ? $"Step {step} of 4"
                    : $"Schritt {step} von 4";
            }
            SetPhase("Systeminformationen und Sicherheit werden geladen", "Loading system information and security", 2);

            async Task LoadSystemAsync()
            {
                try
                {
                    _cachedSnapshot = await StartupPerformanceTracker.MeasureAsync(
                        "Systeminformationen",
                        () => SystemInfoProvider.GetFullSnapshotAsync(cancellationToken));
                    ApplySnapshot(_cachedSnapshot);
                }
                catch (Exception ex) { Logger.LogError("Startladen Systeminformationen", ex); }
            }

            var securityTask = SystemInfoProvider.GetFastSecurityStatusAsync(cancellationToken);
            var systemTask = LoadSystemAsync();
            Task storageTask = _currentPageKey == "Storage" ? LoadStorage() : Task.CompletedTask;

            async Task FinishSystemStageAsync()
            {
                (string Antivirus, string Firewall)? fastSecurity = null;
                try
                {
                    fastSecurity = await securityTask;
                    cancellationToken.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex) { Logger.LogError("Schnelle Sicherheitsprüfung", ex); }

                await systemTask;

                // GetFullSnapshotAsync lädt die langsamen Sicherheitsdetails
                // bewusst noch nicht. Deshalb den schnellen Status erst nach
                // dem übrigen Snapshot anwenden, damit leere Detailwerte ihn
                // nicht wieder mit „Nicht prüfbar“ überschreiben.
                if (fastSecurity is { } security)
                {
                    if (_cachedSnapshot != null)
                    {
                        _cachedSnapshot.DefenderStatus = security.Antivirus;
                        _cachedSnapshot.FirewallStatus = security.Firewall;
                    }
                    ApplyDashboardSecurityStatus(security.Antivirus, security.Firewall);
                    Logger.Log($"Sicherheitsstatus: Virenschutz={security.Antivirus}, Firewall={security.Firewall}");
                }
                if (_cachedSnapshot != null)
                {
                    var snapshotForCache = _cachedSnapshot.Clone();
                    _ = Task.Run(() => StartupSnapshotCache.Save(snapshotForCache));
                }
            }

            await FinishSystemStageAsync();
            if (cancellationToken.IsCancellationRequested) return;

            SetPhase("Dashboard wird vorbereitet", "Preparing dashboard", 3);

            if (_currentPageKey == "Storage")
            {
                StartupStatusText.Text = Localization.CurrentLanguage == "en"
                    ? "Step 4 of 4 · Analyzing files"
                    : "Schritt 4 von 4 · Dateien werden analysiert";
            }
            await storageTask;
            if (cancellationToken.IsCancellationRequested) return;

            await StartupPerformanceTracker.MeasureAsync("Dashboard", PopulateDashboardWidgetsAsync);
            SetPhase("Start abgeschlossen", "Startup complete", 4);
            Logger.Log($"Alle Startdaten geladen: {timer.ElapsedMilliseconds} ms.");
        }

        private void HideStartupOverlay()
        {
            // Das Hauptfenster unter dem noch vollständig sichtbaren Overlay auf
            // seine endgültige Größe bringen. So erscheint niemals kurz der
            // Hauptinhalt in der kleinen Startfenstergröße.
            if (_postStartupWindowRect is { } targetRect)
            {
                AppWindow.MoveAndResize(targetRect);
                _postStartupWindowRect = null;
            }

            var animation = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(UiMetrics.MotionDurationMs)
            };

            var storyboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animation, StartupOverlay);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animation, "Opacity");
            storyboard.Children.Add(animation);

            storyboard.Completed += (_, __) =>
            {
                _startupHexStoryboard?.Stop();
                _startupHexStoryboard = null;

                if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
                {
                    presenter.IsResizable = true;
                    presenter.IsMaximizable = true;
                }

                DispatcherQueue.TryEnqueue(() =>
                {
                    StartupOverlay.Visibility = Visibility.Collapsed;
                    StartupGlassBandsHost.Children.Clear();
                });
            };
            storyboard.Begin();
        }

        // Hebt den Sidebar-Button der aktuell aktiven Seite mit der
        // Akzentfarbe hervor, alle anderen bleiben transparent.
        private void UpdateActiveNavHighlight(string title)
        {
            var accentOverlay = (SolidColorBrush)RootGrid.Resources["AppAccentOverlay20"];
            var transparent = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

            var navButtons = new (Button Button, string Page)[]
            {
                (NavOverviewButton, "Übersicht"),
                (NavSystemButton, "System"),
                (NavUpdatesButton, "Updates"),
                (NavCleanerButton, "Storage"),
                (NavUninstallButton, "Uninstall"),
                (NavChangesButton, "Changes"),
                (NavHistoryButton, "History"),
                (NavAutostartButton, "Autostart"),
            };

            foreach (var (button, page) in navButtons)
            {
                bool active = page == title;
                button.Background = active ? accentOverlay : transparent;
                button.BorderBrush = active
                    ? (SolidColorBrush)RootGrid.Resources["AppAccentBrushLight"]
                    : transparent;
                button.BorderThickness = active ? new Thickness(4, 0, 0, 0) : new Thickness(0);
            }
        }

        // Übersetzt den internen Routing-Namen (bleibt aus Kompatibilitätsgründen
        // z.B. in AppSettings/StartupPage unverändert) in den sauberen, mit der
        // Sidebar konsistenten Anzeigetitel für die große Kopfzeile.
        private static string GetPageDisplayTitle(string internalKey) => internalKey switch
        {
            "Übersicht" => Localization.T("PageTitle.Dashboard"),
            "System" => Localization.T("PageTitle.System"),
            "Updates" => Localization.T("PageTitle.Updates"),
            "Storage" => Localization.T("PageTitle.Storage"),
            "Uninstall" => Localization.T("PageTitle.Uninstall"),
            "History" => Localization.CurrentLanguage == "en" ? "History" : "Verlauf",
            "Autostart" => "Autostart",
            "Changes" => Localization.CurrentLanguage == "en" ? "Changes" : "Veränderungen",
            _ => internalKey
        };

        private void SetPage(string title)
        {
            if (_isUpdatingWinget && title != "Updates")
            {
                ShowInfo(Localization.CurrentLanguage == "en"
                    ? "Finish or cancel the running update before changing pages."
                    : "Beende oder brich das laufende Update ab, bevor du die Seite wechselst.",
                    InfoBarSeverity.Warning);
                return;
            }
            if (!string.IsNullOrWhiteSpace(_currentPageKey))
                _pageScrollOffsets[_currentPageKey] = MainContentScrollViewer.VerticalOffset;
            _currentPageKey = title;
            PageTitle.Text = GetPageDisplayTitle(title);
            PageSubtitle.Text = "";

            OverviewPanel.Visibility = title == "Übersicht" ? Visibility.Visible : Visibility.Collapsed;
            SystemPanel.Visibility = title == "System" ? Visibility.Visible : Visibility.Collapsed;
            ContentArea.Visibility = title == "Updates" ? Visibility.Visible : Visibility.Collapsed;
            StoragePanel.Visibility = title == "Storage" ? Visibility.Visible : Visibility.Collapsed;
            UninstallPanel.Visibility = title == "Uninstall" ? Visibility.Visible : Visibility.Collapsed;
            HistoryPanel.Visibility = title == "History" ? Visibility.Visible : Visibility.Collapsed;
            AutostartPanel.Visibility = title == "Autostart" ? Visibility.Visible : Visibility.Collapsed;
            ChangesPanel.Visibility = title == "Changes" ? Visibility.Visible : Visibility.Collapsed;

            UpdateActiveNavHighlight(title);

            AppsActionBar.Visibility = title == "Updates" ? Visibility.Visible : Visibility.Collapsed;
            DashboardCustomizeHeaderButton.Visibility = title == "Übersicht" ? Visibility.Visible : Visibility.Collapsed;
            SystemActionBar.Visibility = title == "System" ? Visibility.Visible : Visibility.Collapsed;
            StorageActionBar.Visibility = title == "Storage" ? Visibility.Visible : Visibility.Collapsed;
            UninstallActionBar.Visibility = title == "Uninstall" ? Visibility.Visible : Visibility.Collapsed;
            UpdateProgressPanel.Visibility = Visibility.Collapsed;
            StorageProgressPanel.Visibility = Visibility.Collapsed;
            UninstallStatusPanel.Visibility = Visibility.Collapsed;

            ContentArea.Children.Clear();
            StoragePanel.Children.Clear();
            UninstallPanel.Children.Clear();
            if (title != "Uninstall")
            {
                _uninstallIconCards.Clear();
                _loadedUninstallIcons.Clear();
            }

            if (title != "System" && title != "Übersicht")
                _liveUsageTimer?.Stop();

            FadeIn(title switch
            {
                "Übersicht" => OverviewPanel,
                "System" => SystemPanel,
                "Updates" => ContentArea,
                "Storage" => StoragePanel,
                "Uninstall" => UninstallPanel,
                "History" => HistoryPanel,
                "Autostart" => AutostartPanel,
                "Changes" => ChangesPanel,
                _ => null
            });

            double targetOffset = _pageScrollOffsets.TryGetValue(title, out double savedOffset) ? savedOffset : 0;
            DispatcherQueue.TryEnqueue(() =>
            {
                MainContentScrollViewer.ChangeView(null, targetOffset, null, disableAnimation: true);
                switch (title)
                {
                    case "Updates": WingetSearchBox.Focus(FocusState.Programmatic); break;
                    case "Uninstall": UninstallSearchBox.Focus(FocusState.Programmatic); break;
                    case "System": RefreshSystemInfoButton.Focus(FocusState.Programmatic); break;
                    case "Storage": StorageSelectAllButton.Focus(FocusState.Programmatic); break;
                    case "History": HistoryAllButton.Focus(FocusState.Programmatic); break;
                }
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
                Duration = TimeSpan.FromMilliseconds(UiMetrics.MotionDurationMs),
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

        private async Task<bool> ConfirmAsync(string title, string message, string primaryButtonText = "Löschen", bool respectDeleteConfirmationSetting = true, XamlRoot? dialogRoot = null)
        {
            if (respectDeleteConfirmationSetting && !_settings.ShowDeleteConfirmations) return true;

            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                PrimaryButtonText = primaryButtonText,
                CloseButtonText = "Abbrechen",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = dialogRoot ?? this.Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        }

        private void History_Click(object sender, RoutedEventArgs e)
        {
            SetPage("History");
            RenderHistoryPage();
        }

        private void HistoryChip_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string filter)
            {
                _historyFilter = filter;
                RenderHistoryPage();
            }
        }

        private void ShowInfo(string message, InfoBarSeverity severity = InfoBarSeverity.Informational)
        {
            _infoBarDismissCancellation?.Cancel();
            _infoBarDismissCancellation?.Dispose();
            _infoBarDismissCancellation = null;

            AppInfoBar.Message = message;
            AppInfoBar.Content = null;
            AppInfoBar.ActionButton = null;
            AppInfoBar.Severity = severity;
            AppInfoBar.IsOpen = true;

            // Abschluss- und Erfolgshinweise verschwinden von selbst. Warnungen
            // und Fehler bleiben offen, bis sie bewusst geschlossen werden.
            if (severity is InfoBarSeverity.Informational or InfoBarSeverity.Success)
            {
                _infoBarDismissCancellation = new CancellationTokenSource();
                var progress = new ProgressBar
                {
                    Minimum = 0,
                    Maximum = 100,
                    Value = 100,
                    Height = 3
                };
                var content = new StackPanel { Spacing = 5 };
                content.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
                content.Children.Add(progress);
                AppInfoBar.Message = "";
                AppInfoBar.Content = content;
                _ = AnimateUndoProgressAsync(progress, _infoBarDismissCancellation.Token);
                _ = DismissInfoBarAfterDelayAsync(_infoBarDismissCancellation.Token);
            }

        }

        private void ShowUndoInfo(string message, Action undo)
        {
            ShowInfo(message, InfoBarSeverity.Informational);
            var progress = new ProgressBar
            {
                Minimum = 0,
                Maximum = 100,
                Value = 100,
                Height = 3
            };
            var content = new StackPanel { Spacing = 5 };
            content.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
            content.Children.Add(progress);
            AppInfoBar.Message = "";
            AppInfoBar.Content = content;
            var undoButton = new Button
            {
                Content = Localization.CurrentLanguage == "en" ? "Undo" : "Rückgängig",
                Padding = new Thickness(10, 5, 10, 5)
            };
            undoButton.Click += (_, __) =>
            {
                undo();
                AppInfoBar.IsOpen = false;
                AppInfoBar.ActionButton = null;
            };
            AppInfoBar.ActionButton = undoButton;
            if (_infoBarDismissCancellation != null)
                _ = AnimateUndoProgressAsync(progress, _infoBarDismissCancellation.Token);
        }

        private static async Task AnimateUndoProgressAsync(
            ProgressBar progress,
            CancellationToken cancellationToken)
        {
            try
            {
                const int steps = 100;
                for (int step = steps; step >= 0; step--)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    progress.Value = step;
                    await Task.Delay(100, cancellationToken);
                }
            }
            catch (OperationCanceledException) { }
        }

        private async Task DismissInfoBarAfterDelayAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                if (!cancellationToken.IsCancellationRequested)
                    AppInfoBar.IsOpen = false;
            }
            catch (OperationCanceledException)
            {
                // Ein neuer Hinweis ersetzt den vorherigen Timer.
            }
        }

        private void ApplyResponsiveLayout(Size windowSize)
        {
            _compactHeightState = _compactHeightState switch
            {
                null => windowSize.Height < 760,
                true when windowSize.Height > 790 => false,
                false when windowSize.Height < 730 => true,
                _ => _compactHeightState
            };
            bool compact = _compactHeightState == true;
            foreach (var button in new[]
            {
                NavOverviewButton, NavSystemButton, NavUpdatesButton, NavCleanerButton, NavUninstallButton, NavChangesButton
            })
            {
                button.MinHeight = compact ? 44 : 56;
                button.Padding = compact ? new Thickness(14, 9, 14, 9) : new Thickness(18, 14, 18, 14);
                button.Margin = new Thickness(0, 0, 0, compact ? 6 : 12);
            }

            _narrowLayoutState = _narrowLayoutState switch
            {
                // Die Aktionsleiste benötigt zusammen mit der breiten Sidebar
                // deutlich mehr Platz als der reine Inhaltsbereich vermuten lässt.
                // Deshalb früher in die zweite Kopfzeile umbrechen.
                null => windowSize.Width < 1450,
                true when windowSize.Width > 1490 => false,
                false when windowSize.Width < 1410 => true,
                _ => _narrowLayoutState
            };
            bool narrow = _narrowLayoutState == true;
            bool veryNarrow = windowSize.Width < 820;
            PageTitle.Text = narrow && _currentPageKey == "Updates"
                ? (Localization.CurrentLanguage == "en" ? "Updates" : "Updates")
                : narrow && _currentPageKey == "Uninstall"
                    ? (Localization.CurrentLanguage == "en" ? "Apps" : "Programme")
                    : GetPageDisplayTitle(_currentPageKey);
            MainCard.Padding = new Thickness(veryNarrow ? 10 : narrow ? 14 : 20);
            PageTitle.FontSize = veryNarrow ? 26 : narrow ? 30 : 34;
            WingetSearchBox.Width = veryNarrow ? 140 : narrow ? 200 : 280;
            UninstallSearchBox.Width = veryNarrow ? 140 : narrow ? 200 : 280;
            UpdateChannelButton.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
            WingetSelectAllText.Visibility = veryNarrow ? Visibility.Collapsed : Visibility.Visible;
            WingetSelectAllButton.MinWidth = veryNarrow ? 42 : 154;
            UninstallExportButton.Visibility = veryNarrow ? Visibility.Collapsed : Visibility.Visible;
            SysCpuUsageBar.Width = veryNarrow ? 100 : narrow ? 170 : 260;
            SysRamUsageBar.Width = veryNarrow ? 100 : narrow ? 170 : 260;
            WingetSelectAllButton.Padding = narrow ? new Thickness(8, 6, 8, 6) : new Thickness(16, 10, 16, 10);
            StartUpdateButton.Padding = narrow ? new Thickness(10, 6, 10, 6) : new Thickness(16, 10, 16, 10);
            DashboardCustomizeHeaderButton.Content = narrow
                ? (Localization.CurrentLanguage == "en" ? "Customize" : "Anpassen")
                : (Localization.CurrentLanguage == "en" ? "Customize dashboard" : "Dashboard anpassen");
            ToolTipService.SetToolTip(DashboardCustomizeHeaderButton,
                Localization.CurrentLanguage == "en" ? "Customize dashboard" : "Dashboard anpassen");
            DashboardCustomizeHeaderButton.Width = double.NaN;
            DashboardCustomizeHeaderButton.Padding = new Thickness(12, 7, 12, 7);

            // Die Dashboard-Anpassung gehört direkt in die Titelzeile. Eine
            // eigene zweite Zeile mit nur einem Zahnrad wirkte wie ein
            // losgelöstes Bedienelement.
            Grid.SetRow(DashboardCustomizeHeaderButton, 0);
            Grid.SetColumn(DashboardCustomizeHeaderButton, 1);
            Grid.SetColumnSpan(DashboardCustomizeHeaderButton, 1);
            DashboardCustomizeHeaderButton.HorizontalAlignment = HorizontalAlignment.Right;

            foreach (FrameworkElement bar in new FrameworkElement[] { AppsActionBar, SystemActionBar, StorageActionBar, UninstallActionBar })
            {
                Grid.SetRow(bar, narrow ? 1 : 0);
                Grid.SetColumn(bar, narrow ? 0 : 1);
                Grid.SetColumnSpan(bar, narrow ? 2 : 1);
                bar.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            }

            // Nur anhand der logischen Breite umschalten. Die Fensterhöhe und
            // hohe Windows-Skalierung ließen die Sidebar sonst selbst bei einem
            // sichtbar großen Fenster irrtümlich in den Symbolmodus springen.
            // Schon früher auf die Symbolleiste wechseln: Bei 940 logischen
            // Pixeln war zwar die Navigation noch lesbar, für die fünf
            // Dashboard-Karten blieb jedoch zu wenig Platz. Das führte zu
            // abgeschnittenen Werten und Überschriften.
            bool iconOnly = windowSize.Width < 1180;
            SidebarColumn.Width = new GridLength(iconOnly ? 88 : 280);
            SidebarCard.Padding = new Thickness(iconOnly ? 10 : 20);
            // Im schmalen Symbolmodus würde die überlagernde WinUI-Scrollbar
            // direkt über den Navigationsicons liegen. Scrollen per Mausrad,
            // Touch und Tastatur bleibt auch ohne sichtbare Leiste möglich.
            SidebarNavigationScrollViewer.VerticalScrollBarVisibility = iconOnly
                ? ScrollBarVisibility.Hidden
                : ScrollBarVisibility.Auto;
            DispatcherQueue.TryEnqueue(UpdateSidebarScrollHints);
            foreach (var label in new[]
            {
                LblNavDashboard, LblNavSystem, LblNavUpdates, LblNavFiles, LblNavUninstall,
                LblNavChanges, LblNavAutostart, LblNavHistory, LblNavSettings
            })
                label.Visibility = iconOnly ? Visibility.Collapsed : Visibility.Visible;

            NavVersionText.Visibility = iconOnly ? Visibility.Collapsed : Visibility.Visible;
            NavBetaBadge.Visibility = !iconOnly && IsBetaBuild ? Visibility.Visible : Visibility.Collapsed;
            LblNavChangelogHint.Visibility = iconOnly ? Visibility.Collapsed : Visibility.Visible;
            LblNavContact.Visibility = iconOnly ? Visibility.Collapsed : Visibility.Visible;
            SidebarFooterLinks.Visibility = iconOnly ? Visibility.Collapsed : Visibility.Visible;

            foreach (var button in new[]
            {
                NavOverviewButton, NavSystemButton, NavUpdatesButton, NavCleanerButton, NavUninstallButton,
                NavChangesButton, NavAutostartButton, NavHistoryButton, NavSettingsButton, NavChangelogButton
            })
            {
                button.HorizontalContentAlignment = iconOnly
                    ? HorizontalAlignment.Center
                    : HorizontalAlignment.Left;
                if (iconOnly)
                    button.Padding = new Thickness(0);
                else if (button == NavAutostartButton || button == NavHistoryButton ||
                         button == NavSettingsButton || button == NavChangelogButton)
                    button.Padding = new Thickness(12);
            }

            var dashboardCardPadding = new Thickness(narrow ? 14 : 24);
            foreach (var card in new[] { StatCardUpdates, StatCardCpu, StatCardRam, StatCardGpu, StatCardSecurity })
                card.Padding = dashboardCardPadding;
            HealthCpuText.FontSize = narrow ? 30 : 36;
            HealthRamText.FontSize = narrow ? 30 : 36;
            HealthGpuText.FontSize = narrow ? 30 : 36;
            HealthUpdatesText.FontSize = narrow ? 22 : 24;
            HealthSecurityText.FontSize = narrow ? 22 : 24;

            var statusCards = new[] { StatCardCpu, StatCardRam, StatCardGpu };
            for (int column = 0; column < DashboardStatusGrid.ColumnDefinitions.Count; column++)
                DashboardStatusGrid.ColumnDefinitions[column].Width = column < 3
                    ? new GridLength(1, GridUnitType.Star)
                    : new GridLength(0);

            if (veryNarrow)
            {
                DashboardStatusGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
                DashboardStatusGrid.ColumnDefinitions[1].Width = new GridLength(0);
                DashboardStatusGrid.ColumnDefinitions[2].Width = new GridLength(0);
                Grid.SetRow(StatCardCpu, 0); Grid.SetColumn(StatCardCpu, 0); Grid.SetColumnSpan(StatCardCpu, 1);
                Grid.SetRow(StatCardRam, 1); Grid.SetColumn(StatCardRam, 0); Grid.SetColumnSpan(StatCardRam, 1);
                Grid.SetRow(StatCardGpu, 2); Grid.SetColumn(StatCardGpu, 0); Grid.SetColumnSpan(StatCardGpu, 1);

                if (_dashboardPrimaryGrid != null)
                {
                    _dashboardPrimaryGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
                    _dashboardPrimaryGrid.ColumnDefinitions[1].Width = new GridLength(0);
                    _dashboardPrimaryGrid.ColumnDefinitions[2].Width = new GridLength(0);
                    Grid.SetRow(DashCardStatus, 0); Grid.SetColumn(DashCardStatus, 0); Grid.SetColumnSpan(DashCardStatus, 1);
                    Grid.SetRow(StatCardUpdates, 1); Grid.SetColumn(StatCardUpdates, 0); Grid.SetColumnSpan(StatCardUpdates, 1);
                    Grid.SetRow(StatCardSecurity, 2); Grid.SetColumn(StatCardSecurity, 0); Grid.SetColumnSpan(StatCardSecurity, 1);
                    Grid.SetRow(DashCardDisk, 3); Grid.SetColumn(DashCardDisk, 0); Grid.SetColumnSpan(DashCardDisk, 1);
                }
            }
            else if (narrow)
            {
                DashboardStatusGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
                DashboardStatusGrid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
                DashboardStatusGrid.ColumnDefinitions[2].Width = new GridLength(0);
                Grid.SetRow(StatCardCpu, 0); Grid.SetColumn(StatCardCpu, 0); Grid.SetColumnSpan(StatCardCpu, 1);
                Grid.SetRow(StatCardRam, 0); Grid.SetColumn(StatCardRam, 1); Grid.SetColumnSpan(StatCardRam, 1);
                Grid.SetRow(StatCardGpu, 1); Grid.SetColumn(StatCardGpu, 0); Grid.SetColumnSpan(StatCardGpu, 2);

                if (_dashboardPrimaryGrid != null)
                {
                    _dashboardPrimaryGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
                    _dashboardPrimaryGrid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
                    _dashboardPrimaryGrid.ColumnDefinitions[2].Width = new GridLength(0);
                    Grid.SetRow(DashCardStatus, 0); Grid.SetColumn(DashCardStatus, 0); Grid.SetColumnSpan(DashCardStatus, 2);
                    Grid.SetRow(StatCardUpdates, 1); Grid.SetColumn(StatCardUpdates, 0); Grid.SetColumnSpan(StatCardUpdates, 1);
                    Grid.SetRow(StatCardSecurity, 1); Grid.SetColumn(StatCardSecurity, 1); Grid.SetColumnSpan(StatCardSecurity, 1);
                    Grid.SetRow(DashCardDisk, 2); Grid.SetColumn(DashCardDisk, 0); Grid.SetColumnSpan(DashCardDisk, 2);
                }
            }
            else
            {
                foreach (var column in DashboardStatusGrid.ColumnDefinitions)
                    column.Width = new GridLength(1, GridUnitType.Star);
                for (int i = 0; i < statusCards.Length; i++)
                {
                    Grid.SetRow(statusCards[i], 0);
                    Grid.SetColumn(statusCards[i], i);
                    Grid.SetColumnSpan(statusCards[i], 1);
                }

                if (_dashboardPrimaryGrid != null)
                {
                    foreach (var column in _dashboardPrimaryGrid.ColumnDefinitions)
                        column.Width = new GridLength(1, GridUnitType.Star);
                    Grid.SetRow(DashCardStatus, 0); Grid.SetColumn(DashCardStatus, 0); Grid.SetColumnSpan(DashCardStatus, 3);
                    Grid.SetRow(StatCardUpdates, 1); Grid.SetColumn(StatCardUpdates, 0); Grid.SetColumnSpan(StatCardUpdates, 1);
                    Grid.SetRow(StatCardSecurity, 1); Grid.SetColumn(StatCardSecurity, 1); Grid.SetColumnSpan(StatCardSecurity, 1);
                    Grid.SetRow(DashCardDisk, 1); Grid.SetColumn(DashCardDisk, 2); Grid.SetColumnSpan(DashCardDisk, 1);
                }
            }

            ToolTipService.SetToolTip(NavOverviewButton, iconOnly ? LblNavDashboard.Text : null);
            ToolTipService.SetToolTip(NavSystemButton, iconOnly ? LblNavSystem.Text : null);
            ToolTipService.SetToolTip(NavUpdatesButton, iconOnly ? LblNavUpdates.Text : null);
            ToolTipService.SetToolTip(NavCleanerButton, iconOnly ? LblNavFiles.Text : null);
            ToolTipService.SetToolTip(NavUninstallButton, iconOnly ? LblNavUninstall.Text : null);
            ToolTipService.SetToolTip(NavChangesButton, iconOnly ? LblNavChanges.Text : null);
            ToolTipService.SetToolTip(NavAutostartButton, iconOnly ? LblNavAutostart.Text : null);
            ToolTipService.SetToolTip(NavHistoryButton, iconOnly ? LblNavHistory.Text : null);
            ApplyDashboardCustomizationLayout();
        }

        private void SetupCompactTooltips()
        {
            bool en = Localization.CurrentLanguage == "en";
            ToolTipService.SetToolTip(RefreshButton, en ? "Refresh updates" : "Updates aktualisieren");
            ToolTipService.SetToolTip(StorageRefreshButton, en ? "Analyze storage again" : "Speicher erneut analysieren");
            ToolTipService.SetToolTip(UninstallRefreshButton, en ? "Refresh program list" : "Programmliste aktualisieren");
            ToolTipService.SetToolTip(RefreshSystemInfoButton, en ? "Refresh system information" : "Systeminformationen aktualisieren");
            ToolTipService.SetToolTip(WingetClearSearchButton, en ? "Clear update search" : "Update-Suche leeren");
            ToolTipService.SetToolTip(UninstallClearSearchButton, en ? "Clear program search" : "Programmsuche leeren");
        }

        private void SidebarNavigationScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
            => UpdateSidebarScrollHints();

        private void UpdateSidebarScrollHints()
        {
            bool compact = RootGrid.ActualWidth < 1180;
            bool scrollable = SidebarNavigationScrollViewer.ScrollableHeight > 1;
            SidebarScrollUpHint.Visibility = compact && scrollable && SidebarNavigationScrollViewer.VerticalOffset > 1
                ? Visibility.Visible
                : Visibility.Collapsed;
            SidebarScrollDownHint.Visibility = compact && scrollable &&
                                               SidebarNavigationScrollViewer.VerticalOffset < SidebarNavigationScrollViewer.ScrollableHeight - 1
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void SetGlobalStatus(string? message)
        {
            GlobalStatusBar.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
            GlobalStatusText.Text = message ?? "";
            GlobalStatusRing.IsActive = !string.IsNullOrWhiteSpace(message);
        }


        private Border MakeTitleBarDivider() => new()
        {
            Height = 1,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = (SolidColorBrush)RootGrid.Resources["AppOverlay1E"]
        };

        // Sichtbarer Fenstertitel für Popup-Fenster. Da ExtendsContentIntoTitleBar
        // die vom System gezeichnete Titel-Zeile (Icon+Text) komplett entfernt,
        // müssen wir den Titel selbst anzeigen, sonst ist er unsichtbar.
        private TextBlock MakeTitleBarLabel(string title) => new()
        {
            Text = title,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (SolidColorBrush)RootGrid.Resources["AppForegroundBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(16, 0, 0, 0)
        };

        private async void ContactButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = Localization.T("Nav.Contact"),
                Content = Localization.T("Contact.Body"),
                CloseButtonText = Localization.T("Settings.Close"),
                XamlRoot = this.Content.XamlRoot
            };

            await dialog.ShowAsync();
        }

        private const string KofiUrl = "https://ko-fi.com/winvora";

        private async void KofiButton_Click(object sender, RoutedEventArgs e)
        {
            if (KofiUrl.Contains("DEINNAME"))
            {
                // Platzhalter wurde noch nicht ersetzt - Hinweis statt kaputtem Link.
                var placeholderDialog = new ContentDialog
                {
                    Title = "Ko-fi-Link fehlt noch",
                    Content = "Trag deinen echten Ko-fi-Link in der Konstante \"KofiUrl\" " +
                              "in MainWindow.xaml.cs ein (KofiButton_Click).",
                    CloseButtonText = Localization.T("Settings.Close"),
                    XamlRoot = this.Content.XamlRoot
                };
                await placeholderDialog.ShowAsync();
                return;
            }

            try
            {
                await Windows.System.Launcher.LaunchUriAsync(new Uri(KofiUrl));
            }
            catch (Exception ex)
            {
                Logger.LogError("KofiButton_Click", ex);
            }
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
                _cachedSnapshot = await SystemInfoProvider.GetFullSnapshotAsync(_startupCancellation.Token);
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

        private async Task LoadDeferredProgramDataAsync()
        {
            if (_installedPrograms.Count > 0 || _startupCancellation.IsCancellationRequested) return;
            try
            {
                await Task.Yield();
                _installedPrograms = await StartupPerformanceTracker.MeasureAsync(
                    "Installierte Programme (Hintergrund)",
                    () => Task.Run(() => InstalledProgramsService.GetInstalledPrograms(resolveIcons: false),
                        _startupCancellation.Token));
                if (_startupCancellation.IsCancellationRequested) return;
                DashInstalledCountText.Text = _installedPrograms.Count.ToString();
                if (_pcChangeSummary == null) await LoadPcChangesAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Logger.LogError("Programmliste im Hintergrund laden", ex);
                DashInstalledCountText.Text = "N/A";
            }
        }

        private void ApplyStartupOverlayTheme(bool dark)
        {
            StartupOverlay.Background = new SolidColorBrush(dark
                ? Windows.UI.Color.FromArgb(0xFF, 0x14, 0x14, 0x16)
                : Windows.UI.Color.FromArgb(0xFF, 0xF8, 0xF6, 0xFC));
            StartupBrandText.Foreground = new SolidColorBrush(dark
                ? Microsoft.UI.Colors.White
                : Windows.UI.Color.FromArgb(0xFF, 0x18, 0x16, 0x20));
            StartupStatusCard.Background = new SolidColorBrush(dark
                ? Windows.UI.Color.FromArgb(0xCC, 0x18, 0x18, 0x1D)
                : Windows.UI.Color.FromArgb(0xF5, 0xFF, 0xFF, 0xFF));
            StartupStatusCard.BorderBrush = new SolidColorBrush(dark
                ? Windows.UI.Color.FromArgb(0x35, 0x6C, 0x5C, 0xE7)
                : Windows.UI.Color.FromArgb(0x80, 0x6C, 0x5C, 0xE7));
            StartupStatusCard.BorderThickness = dark ? new Thickness(1) : new Thickness(1.5);
            StartupStatusText.Foreground = new SolidColorBrush(dark
                ? Windows.UI.Color.FromArgb(0xFF, 0xF2, 0xF2, 0xF5)
                : Windows.UI.Color.FromArgb(0xFF, 0x2A, 0x27, 0x31));
            StartupProgressText.Foreground = new SolidColorBrush(dark
                ? Windows.UI.Color.FromArgb(0xBF, 0xFF, 0xFF, 0xFF)
                : Windows.UI.Color.FromArgb(0xB8, 0x2A, 0x27, 0x31));
        }

        private async Task LoadSecurityDetailsAsync()
        {
            if (_securityDetailsLoaded || _securityDetailsLoading || _cachedSnapshot == null) return;
            _securityDetailsLoading = true;
            bool en = Localization.CurrentLanguage == "en";
            SecurityDetailsLoadingText.Text = en ? "Checking security details..." : "Sicherheitsdetails werden geprüft...";
            AutomationProperties.SetName(SecurityDetailsLoadingPanel, SecurityDetailsLoadingText.Text);
            SecurityDetailsGrid.Opacity = 0.25;
            SecurityDetailsLoadingPanel.Visibility = Visibility.Visible;
            try
            {
                await StartupPerformanceTracker.MeasureAsync("Sicherheitsdetails", () =>
                    SystemInfoProvider.RefreshSectionAsync(
                        _cachedSnapshot, SystemInfoSection.Security, _startupCancellation.Token));
                _securityDetailsLoaded = true;
                if (_currentPageKey == "System") ApplySnapshot(_cachedSnapshot);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Logger.LogError("Sicherheitsdetails laden", ex); }
            finally
            {
                _securityDetailsLoading = false;
                SecurityDetailsLoadingPanel.Visibility = Visibility.Collapsed;
                SecurityDetailsGrid.Opacity = 1;
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
                HealthUpdatesText.Text = _cachedPackages.Count == 0 ? Localization.T("Common.None") : _cachedPackages.Count.ToString();
                UpdateDashboardStatusSummary();
                return;
            }

            HealthUpdatesText.Text = Localization.T("Common.Checking");

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
            if (_cachedSnapshot == null)
            {
                await LoadSystemSnapshotIfNeededAsync("Wird aktualisiert...", "Fehler beim Aktualisieren der Systeminfos");
                return;
            }

            var openSections = new List<SystemInfoSection>();
            if (DeviceExpander.IsExpanded) openSections.Add(SystemInfoSection.Device);
            if (OsExpander.IsExpanded) openSections.Add(SystemInfoSection.OperatingSystem);
            if (CpuExpander.IsExpanded) openSections.Add(SystemInfoSection.Cpu);
            if (RamExpander.IsExpanded) openSections.Add(SystemInfoSection.Ram);
            if (BoardExpander.IsExpanded) openSections.Add(SystemInfoSection.Board);
            if (SecurityExpander.IsExpanded) openSections.Add(SystemInfoSection.Security);
            if (GpuExpander.IsExpanded) openSections.Add(SystemInfoSection.Gpu);
            if (DrivesExpander.IsExpanded) openSections.Add(SystemInfoSection.Drives);
            if (NetworkExpander.IsExpanded) openSections.Add(SystemInfoSection.Network);
            if (BatteryExpander.IsExpanded) openSections.Add(SystemInfoSection.Battery);

            if (openSections.Count == 0)
            {
                ShowInfo(Localization.CurrentLanguage == "en"
                    ? "Open a system information category first."
                    : "Öffne zuerst eine Systeminfo-Kategorie.");
                return;
            }

            RefreshSystemInfoButton.IsEnabled = false;
            PageSubtitle.Text = Localization.CurrentLanguage == "en" ? "Refreshing open categories..." : "Geöffnete Kategorien werden aktualisiert...";
            try
            {
                await Task.WhenAll(openSections.Select(section => SystemInfoProvider.RefreshSectionAsync(
                    _cachedSnapshot, section, _startupCancellation.Token)));
                ApplySnapshot(_cachedSnapshot);
            }
            catch (Exception ex)
            {
                Logger.LogError("Geöffnete Systeminfo-Kategorien aktualisieren", ex);
                ShowInfo(ex.Message, InfoBarSeverity.Error);
            }
            finally { RefreshSystemInfoButton.IsEnabled = true; }
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







    }

}
