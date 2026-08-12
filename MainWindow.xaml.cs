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
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI;
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
        private CancellationTokenSource? _wingetUpdateCancellation;
        private readonly WingetUpdateService _wingetUpdateService = new();
        private List<WingetPackage>? _cachedPackages;
        private bool _isDarkTheme = true;

        private readonly List<(WingetPackage Package, ToggleSwitch Toggle, ToolkitControls.SettingsCard Card, string BaseDescription)> _wingetRows = new();
        private readonly List<(StorageCategory Category, ToggleSwitch Toggle)> _storageRows = new();
        private TextBlock? _wingetNoResultsText;
        private TextBlock? _uninstallNoResultsText;
        private AppSettings _settings = AppSettings.Load();
        private Microsoft.UI.Xaml.Media.Animation.Storyboard? _startupHexStoryboard;

        // Eine zentrale Versionsquelle: <Version> in WinVora.csproj. So können
        // Sidebar, Einstellungen und Updatevergleich nicht mehr auseinanderlaufen.
        private static readonly string CurrentVersion =
            Assembly.GetExecutingAssembly().GetName().Version is { } version
                ? $"{version.Major}.{version.Minor}.{version.Build}"
                : "0.0.0";

        // Vom Hintergrund-Check gefundenes Update (falls vorhanden) - damit
        // das Einstellungen-Fenster nicht nochmal extra suchen muss.
        private UpdateInfo? _pendingUpdateInfo;
        private string _currentPageKey = "Übersicht";
        private string _historyFilter = "All";
        private readonly Dictionary<string, double> _pageScrollOffsets = new();


        public MainWindow()
        {
            this.InitializeComponent();
            SetupSystemInfoCopyButtons();
            RootGrid.SizeChanged += (_, args) => ApplyResponsiveLayout(args.NewSize);
            this.Title = "WinVora";
            NavVersionText.Text = $"Version {CurrentVersion}";
            this.Activated += MainWindow_Activated;
            this.Closed += (_, __) =>
            {
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
            ApplyTheme(_settings.DarkMode, persist: false);

            SetupOverviewCardHoverEffects();

            Localization.CurrentLanguage = _settings.Language;
            ApplyLanguage();
            RestoreWindowPlacement();
            SetupKeyboardShortcuts();
            UpdateService.CleanupOldDownloads();
        }

        private void RestoreWindowPlacement()
        {
            try
            {
                var point = new Windows.Graphics.PointInt32(_settings.WindowX ?? 100, _settings.WindowY ?? 100);
                var display = Microsoft.UI.Windowing.DisplayArea.GetFromPoint(
                    point, Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
                var work = display.WorkArea;
                int width = Math.Min(_settings.WindowWidth, work.Width);
                int height = Math.Min(_settings.WindowHeight, work.Height);
                int x = Math.Clamp(_settings.WindowX ?? work.X + 60, work.X, work.X + work.Width - width);
                int y = Math.Clamp(_settings.WindowY ?? work.Y + 60, work.Y, work.Y + work.Height - height);
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

            RootGrid.KeyboardAccelerators.Add(refresh);
            RootGrid.KeyboardAccelerators.Add(search);
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
            LiveDashboardCard.Header = Localization.T("Dash.Header");
            LblDashDisk.Text = Localization.T("Dash.Disk");
            LblDashTemp.Text = Localization.T("Dash.Temp");
            LblDashPrograms.Text = Localization.T("Dash.Programs");
            LblDashCleanup.Text = Localization.T("Dash.Cleanup");
            LblDashUpdatesAvailable.Text = Localization.T("Dash.UpdatesAvailable");
            LblDashRam.Text = Localization.T("Dash.Ram");
            LblDashStatus.Text = Localization.T("Dash.Status");

            // Verlaufsdiagramme + Aktivitätsverlauf
            HistoryCard.Text = Localization.T("Dash.HistoryHeader");
            LblHistoryCpu.Text = Localization.T("Stat.Cpu");
            LblHistoryRam.Text = Localization.T("Stat.Ram");
            LblHistoryGpu.Text = Localization.T("Stat.Gpu");

            // "Nicht verfügbar"-Platzhalter neu setzen, falls sie gerade aktiv sind
            if (DashTempText.Text is "Nicht verfügbar" or "Not available")
                DashTempText.Text = Localization.T("Dash.NotAvailable");

            // Systeminfo: Action-Bar + Abschnitts-Überschriften
            RefreshSystemInfoButton.Content = Localization.T("System.Refresh");
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
            RefreshButton.Content = Localization.T("Common.Refresh");
            StartUpdateButton.Content = Localization.T("Winget.StartUpdate");

            // Storage: Action-Bar
            StorageRefreshButton.Content = Localization.T("Common.Refresh");
            StorageDeleteSelectedButton.Content = Localization.T("Storage.DeleteSelected");

            // Deinstaller: Action-Bar
            UninstallSearchBox.PlaceholderText = Localization.T("Uninstall.SearchPlaceholder");
            UninstallRefreshButton.Content = Localization.T("Common.Refresh");

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
            StartStartupGlassBarAnimation();
        }

        // Dezente Hover-Animation UND weicher Schatten für die Dashboard-/
        // Statuskarten auf der Übersicht. ThemeShadow braucht eine leichte
        // Z-Anhebung (Translation), sonst wird kein Schatten gerendert.
        private void AttachCardHoverEffect(Border card)
        {
            var originalBorderBrush = card.BorderBrush;
            var hoverBrush = (SolidColorBrush)RootGrid.Resources["AppAccentBrushLight"];

            card.PointerEntered += (_, __) => card.BorderBrush = hoverBrush;
            card.PointerExited += (_, __) => card.BorderBrush = originalBorderBrush;

            card.Shadow = new ThemeShadow();
            card.Translation = new System.Numerics.Vector3(0, 0, 16);
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
            const double hexSize = 38;
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
            if (overlayWidth <= 0) overlayWidth = 1200;
            if (overlayHeight <= 0) overlayHeight = 720;
            StartupGlassBandsHost.CacheMode = new BitmapCache();

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

            var sharedStrokeBrush = new SolidColorBrush(
                Windows.UI.Color.FromArgb(0x18, AccentColorPrimary.R, AccentColorPrimary.G, AccentColorPrimary.B));
            var sharedFillBrush = new SolidColorBrush(
                Windows.UI.Color.FromArgb(0x08, AccentColorPrimary.R, AccentColorPrimary.G, AccentColorPrimary.B));
            var glowStrokeBrush = new SolidColorBrush(
                Windows.UI.Color.FromArgb(0xE0, AccentColorLight.R, AccentColorLight.G, AccentColorLight.B));
            var transparentFillBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

            // Zweite, deckungsgleiche Ebene: Ein schmaler bewegter Clip macht
            // immer nur einen Ausschnitt sichtbar. Dadurch läuft die helle
            // Welle wieder über die Waben, benötigt aber weiterhin nur eine
            // einzige Animation.
            var glowCanvas = new Canvas
            {
                Width = overlayWidth,
                Height = overlayHeight,
                IsHitTestVisible = false
            };

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

                    var glowHex = CreateHexCell(hexSize, glowStrokeBrush, transparentFillBrush);
                    Canvas.SetLeft(glowHex, x);
                    Canvas.SetTop(glowHex, y);
                    glowCanvas.Children.Add(glowHex);
                }
            }

            const double glowBandWidth = 240;
            double glowBandLength = Math.Sqrt(overlayWidth * overlayWidth + overlayHeight * overlayHeight) * 1.5;
            var clipTransform = new CompositeTransform
            {
                Rotation = -45,
                TranslateX = -glowBandWidth,
                TranslateY = overlayHeight + glowBandWidth
            };
            glowCanvas.Clip = new RectangleGeometry
            {
                Rect = new Rect(-glowBandWidth / 2, -glowBandLength / 2, glowBandWidth, glowBandLength),
                Transform = clipTransform
            };
            StartupGlassBandsHost.Children.Add(glowCanvas);
            StartSharedHexAnimation(clipTransform, overlayWidth, overlayHeight, glowBandWidth);
        }

        // Ein einzelnes Sechseck: dünner Umriss (per SolidColorBrush, wird für
        // den Leucht-Effekt separat animiert), ganz leichte Füllung.
        private Microsoft.UI.Xaml.Shapes.Polygon CreateHexCell(
            double size,
            SolidColorBrush strokeBrush,
            SolidColorBrush fillBrush)
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
                StrokeThickness = 1.4,
                Fill = fillBrush
            };
        }

        // Animiert die Umriss-Farbe eines Sechsecks von dunklem zu hellem,
        // leuchtendem Lila und zurück - das eigentliche "Leuchten". Die
        // Verzögerung (0-1, Position entlang der Diagonalen unten-links nach
        // oben-rechts) sorgt dafür, dass alle Zellen zusammen wie eine
        // durchlaufende Farbwelle wirken statt unabhängig voneinander zu blinken.
        private void StartSharedHexAnimation(
            CompositeTransform clipTransform,
            double overlayWidth,
            double overlayHeight,
            double glowBandWidth)
        {
            var animationX = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                From = -glowBandWidth,
                To = overlayWidth + glowBandWidth,
                Duration = TimeSpan.FromSeconds(3.8),
                RepeatBehavior = Microsoft.UI.Xaml.Media.Animation.RepeatBehavior.Forever,
                EnableDependentAnimation = true
            };
            var animationY = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                From = overlayHeight + glowBandWidth,
                To = -glowBandWidth,
                Duration = TimeSpan.FromSeconds(3.8),
                RepeatBehavior = Microsoft.UI.Xaml.Media.Animation.RepeatBehavior.Forever,
                EnableDependentAnimation = true
            };

            _startupHexStoryboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animationX, clipTransform);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animationX, "TranslateX");
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animationY, clipTransform);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animationY, "TranslateY");
            _startupHexStoryboard.Children.Add(animationX);
            _startupHexStoryboard.Children.Add(animationY);
            _startupHexStoryboard.Begin();
        }

        // Startet die Diagonal-Bewegung samt sanftem "Atmen" für ein einzelnes Band.
        private void LogActivity(string iconGlyph, string textDe, string textEn)
        {
            _settings.ActivityLog.Insert(0, new ActivityLogEntry
            {
                TimestampUtc = DateTime.UtcNow,
                IconGlyph = iconGlyph,
                TextDe = textDe,
                TextEn = textEn
            });

            // Nur die letzten 20 Einträge behalten, damit die Datei nicht
            // unbegrenzt wächst.
            while (_settings.ActivityLog.Count > 20)
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

            if (RootGrid.Resources["AppRootBackgroundBrush"] is SolidColorBrush rootBrush)
                rootBrush.Color = dark
                    ? Microsoft.UI.ColorHelper.FromArgb(0xF0, 0x00, 0x00, 0x00)
                    : Microsoft.UI.ColorHelper.FromArgb(0xF0, 0xFF, 0xFF, 0xFF);

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
        // Zeigt beim allerersten Start eine einmalige Sprachauswahl. Wird nur
        // gezeigt, solange _settings.HasChosenLanguage noch false ist.
        private async Task ShowFirstRunLanguagePromptAsync()
        {
            var languageCombo = new ComboBox
            {
                Width = 220,
                HorizontalAlignment = HorizontalAlignment.Center
            };
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

            HideStartupOverlay();

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
                var update = await UpdateService.CheckForUpdateAsync(CurrentVersion);
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

            StartupStatusText.Text = Localization.T("Common.LoadingSystemInfo");

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

            StartupStatusText.Text = Localization.T("Common.CheckingUpdates");

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

            _ = PopulateDashboardWidgetsAsync();

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

            storyboard.Completed += (_, __) =>
            {
                StartupOverlay.Visibility = Visibility.Collapsed;
                _startupHexStoryboard?.Stop();
                _startupHexStoryboard = null;
                StartupGlassBandsHost.Children.Clear();
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
            _ => internalKey
        };

        private void SetPage(string title)
        {
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

            UpdateActiveNavHighlight(title);

            AppsActionBar.Visibility = title == "Updates" ? Visibility.Visible : Visibility.Collapsed;
            SystemActionBar.Visibility = title == "System" ? Visibility.Visible : Visibility.Collapsed;
            StorageActionBar.Visibility = title == "Storage" ? Visibility.Visible : Visibility.Collapsed;
            UninstallActionBar.Visibility = title == "Uninstall" ? Visibility.Visible : Visibility.Collapsed;
            UpdateProgressPanel.Visibility = Visibility.Collapsed;
            StorageProgressPanel.Visibility = Visibility.Collapsed;

            ContentArea.Children.Clear();
            StoragePanel.Children.Clear();
            UninstallPanel.Children.Clear();

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
                _ => null
            });

            double targetOffset = _pageScrollOffsets.TryGetValue(title, out double savedOffset) ? savedOffset : 0;
            DispatcherQueue.TryEnqueue(() =>
                MainContentScrollViewer.ChangeView(null, targetOffset, null, disableAnimation: true));
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
                Duration = TimeSpan.FromMilliseconds(160),
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

        private async Task<bool> ConfirmAsync(string title, string message, string primaryButtonText = "Löschen", bool respectDeleteConfirmationSetting = true)
        {
            if (respectDeleteConfirmationSetting && !_settings.ShowDeleteConfirmations) return true;

            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                PrimaryButtonText = primaryButtonText,
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
            AttachCardHoverEffect(card);
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
            AppInfoBar.Message = message;
            AppInfoBar.Severity = severity;
            AppInfoBar.IsOpen = true;
        }

        private void ApplyResponsiveLayout(Size windowSize)
        {
            bool compact = windowSize.Height < 760;
            foreach (var button in new[]
            {
                NavOverviewButton, NavSystemButton, NavUpdatesButton, NavCleanerButton, NavUninstallButton
            })
            {
                button.MinHeight = compact ? 44 : 56;
                button.Padding = compact ? new Thickness(14, 9, 14, 9) : new Thickness(18, 14, 18, 14);
                button.Margin = new Thickness(0, 0, 0, compact ? 6 : 12);
            }

            bool narrow = windowSize.Width < 1180;
            WingetSearchBox.Width = narrow ? 150 : 220;
            WingetSelectAllButton.Padding = narrow ? new Thickness(8, 6, 8, 6) : new Thickness(16, 10, 16, 10);
            StartUpdateButton.Padding = narrow ? new Thickness(10, 6, 10, 6) : new Thickness(16, 10, 16, 10);
        }

        private void SetGlobalStatus(string? message)
        {
            GlobalStatusBar.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
            GlobalStatusText.Text = message ?? "";
            GlobalStatusRing.IsActive = !string.IsNullOrWhiteSpace(message);
        }

        private void RenderHistoryPage()
        {
            HistoryListPanel.Children.Clear();
            var entries = _settings.ActivityLog.Where(entry => _historyFilter == "All" || entry.Result == _historyFilter).ToList();
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

            foreach (var entry in entries)
            {
                string text = Localization.CurrentLanguage == "en" ? entry.TextEn : entry.TextDe;
                string details = string.Join(" · ", new[]
                {
                    entry.PackageId,
                    !string.IsNullOrWhiteSpace(entry.OldVersion) ? $"{entry.OldVersion} → {entry.NewVersion}" : null,
                    entry.ExitCode is int exitCode && exitCode != 0 ? $"0x{unchecked((uint)exitCode):X8}" : null
                }.Where(value => !string.IsNullOrWhiteSpace(value)));
                Windows.UI.Color color = entry.Result switch
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
                    string statusText = entry.Result switch
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
                card.MinHeight = 82;
                card.Padding = new Thickness(18, 14, 18, 14);
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
            _cachedSnapshot ??= await SystemInfoProvider.GetFullSnapshotAsync();
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

        private void Autostart_Click(object sender, RoutedEventArgs e)
        {
            SetPage("Autostart");
            RenderAutostartPage();
        }

        private void RenderAutostartPage()
        {
            AutostartListPanel.Children.Clear();
            var entries = AutostartService.GetEntries();
            PageSubtitle.Text = $"{entries.Count} Autostart-Programme";
            foreach (var entry in entries)
            {
                bool targetExists = AutostartService.CommandTargetExists(entry.Command);
                var toggle = new ToggleSwitch
                {
                    IsOn = entry.Enabled,
                    OnContent = Localization.CurrentLanguage == "en" ? "Active" : "Aktiv",
                    OffContent = Localization.CurrentLanguage == "en" ? "Disabled" : "Deaktiviert"
                };
                toggle.Toggled += (_, __) =>
                {
                    try
                    {
                        AutostartService.SetEnabled(entry, toggle.IsOn);
                        ShowInfo(toggle.IsOn
                            ? $"{entry.Name} wurde aktiviert."
                            : $"{entry.Name} wurde deaktiviert.", InfoBarSeverity.Success);
                    }
                    catch (Exception ex) { Logger.LogError($"Autostart {entry.Name}", ex); }
                };
                var statusPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
                statusPanel.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(9, 5, 9, 5),
                    Background = new SolidColorBrush(targetExists
                        ? Windows.UI.Color.FromArgb(0x28, 0x4C, 0xD9, 0x73)
                        : Windows.UI.Color.FromArgb(0x28, 0xFF, 0x6B, 0x6B)),
                    Child = new TextBlock
                    {
                        Text = targetExists ? "OK" : (Localization.CurrentLanguage == "en" ? "File missing" : "Datei fehlt"),
                        Foreground = new SolidColorBrush(targetExists
                            ? Windows.UI.Color.FromArgb(0xFF, 0x4C, 0xD9, 0x73)
                            : Windows.UI.Color.FromArgb(0xFF, 0xFF, 0x6B, 0x6B))
                    }
                });
                statusPanel.Children.Add(toggle);
                AutostartListPanel.Children.Add(new ToolkitControls.SettingsCard
                {
                    Header = entry.Name,
                    Description = (Localization.CurrentLanguage == "en" ? "Path: " : "Pfad: ") + entry.Command,
                    Content = statusPanel,
                    CornerRadius = new CornerRadius(16)
                });
            }
            if (entries.Count == 0)
                AutostartListPanel.Children.Add(MakeEmptyState(
                    "\uE768",
                    Localization.CurrentLanguage == "en" ? "No startup programs" : "Keine Autostart-Programme",
                    Localization.CurrentLanguage == "en" ? "No programs start automatically for this user." : "Für diesen Benutzer starten keine Programme automatisch."));
        }

        private void SaveSecondaryWindowPlacement(Window window, bool settingsWindow)
        {
            var position = window.AppWindow.Position;
            var size = window.AppWindow.Size;
            if (settingsWindow)
            {
                _settings.SettingsWindowX = position.X;
                _settings.SettingsWindowY = position.Y;
                _settings.SettingsWindowWidth = size.Width;
                _settings.SettingsWindowHeight = size.Height;
            }
            else
            {
                _settings.ChangelogWindowX = position.X;
                _settings.ChangelogWindowY = position.Y;
                _settings.ChangelogWindowWidth = size.Width;
                _settings.ChangelogWindowHeight = size.Height;
            }
            _settings.Save();
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

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
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

            var panel = new StackPanel { Spacing = 18, MaxWidth = 420 };

            panel.Children.Add(new TextBlock
            {
                Text = Localization.T("Settings.Title"),
                FontSize = 28,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (SolidColorBrush)RootGrid.Resources["AppForegroundBrush"]
            });

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

            // Heller / Dunkler Modus
            var themeToggle = new ToggleSwitch
            {
                Header = Localization.T("Settings.LightMode"),
                IsOn = !_settings.DarkMode,
                OnContent = Localization.T("Settings.On"),
                OffContent = Localization.T("Settings.Off")
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

            // Reduzierte Bewegung
            var motionToggle = new ToggleSwitch
            {
                Header = Localization.T("Settings.Animations"),
                IsOn = !_settings.ReducedMotion,
                OnContent = Localization.T("Settings.On"),
                OffContent = Localization.T("Settings.Off")
            };
            motionToggle.Toggled += (_, __) =>
            {
                _settings.ReducedMotion = !motionToggle.IsOn;
                _settings.Save();
            };
            cardContent.Children.Add(motionToggle);

            panel.Children.Add(card);

            // ---- Verhalten ----
            var behaviorCard = MakeSettingsCard(Localization.T("Settings.Behavior"), out var behaviorContent);

            // Startseite
            var startupCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            var startupOptions = new (string Value, string Label)[]
            {
                ("Übersicht", "Dashboard"),
                ("System", "Systeminfo"),
                ("Updates", "Winget"),
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

            panel.Children.Add(behaviorCard);

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
                Padding = new Thickness(0, 0, 14, 0),
                Content = panel
            };

            var contentHost = new Grid { Padding = new Thickness(24, 16, 10, 24) };
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
            StyleDarkWindow(settingsWindow, _settings.SettingsWindowWidth, _settings.SettingsWindowHeight);
            WindowActivationService.PlaceWindow(this, settingsWindow,
                _settings.SettingsWindowX, _settings.SettingsWindowY,
                _settings.SettingsWindowWidth, _settings.SettingsWindowHeight);
            settingsWindow.Activate();
            WindowActivationService.ShowOwnedInFront(this, settingsWindow);
        }

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

        private void ChangelogButton_Click(object sender, RoutedEventArgs e)
        {
            if (_changelogWindow != null)
            {
                _changelogWindow.Activate();
                WindowActivationService.ShowOwnedInFront(this, _changelogWindow);
                return;
            }

            _changelogWindow = new Window
            {
                Title = Localization.T("Changelog.WindowTitle")
            };
            var changelogWindow = _changelogWindow;
            changelogWindow.Closed += (_, __) =>
            {
                SaveSecondaryWindowPlacement(changelogWindow, settingsWindow: false);
                _changelogWindow = null;
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
                Spacing = 14
            };

            panel.Children.Add(new TextBlock
            {
                Text = Localization.T("Changelog.WindowTitle"),
                FontSize = 28,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (SolidColorBrush)RootGrid.Resources["AppForegroundBrush"]
            });

            panel.Children.Add(MakeChangelogCard(
    "Version 0.8.2",
    "• Programm-Updates öffnen ihre Hersteller-Installer jetzt sichtbar statt\n" +
    "  unbemerkt im Hintergrund\n" +
    "• Deutliche Warnung vor möglichen Neustarts – mit besonderem Hinweis bei\n" +
    "  der EA App\n" +
    "• Laufende Updates können abgebrochen werden\n" +
    "• Download, Installation und Warten auf den Abschluss werden getrennt angezeigt\n" +
    "• Neuer übersichtlicher Abschlussbericht mit verständlichen Fehlermeldungen\n" +
    "• WinVora erkennt, wenn Windows nach einem Update neu gestartet werden muss\n" +
    "• Der Aktivitätsverlauf enthält jetzt Programm, Version und Ergebnis\n" +
    "• Windows-Benachrichtigung nach abgeschlossenen Programm-Updates\n" +
    "• Systeminformationen lassen sich pro Bereich bequem kopieren\n" +
    "• Updates können für 1, 7 oder 30 Tage zurückgestellt oder dauerhaft\n" +
    "  ignoriert und später gesammelt wieder eingeblendet werden\n" +
    "• Neue Verlaufsseite mit Ergebnisfiltern und Export als Textdatei\n" +
    "• Kompletter Systembericht lässt sich als übersichtliche Datei exportieren\n" +
    "• Neue Autostart-Verwaltung zum sicheren Aktivieren und Deaktivieren von\n" +
    "  Programmen im eigenen Windows-Benutzerkonto\n" +
    "• Einstellungen und Changelog öffnen zuverlässig im Vordergrund und merken\n" +
    "  sich Größe und Position\n" +
    "• Der Ladebildschirm läuft beim Verschieben deutlich flüssiger, behält aber\n" +
    "  seine diagonale Waben-Leuchtwelle\n" +
    "• Mehrere interne Bereiche wurden aufgeräumt und für zukünftige Updates stabiler gemacht",
    "• Program updates now show their publisher installers instead of running\n" +
    "  unnoticed in the background\n" +
    "• Clear warning about possible restarts, including a special warning for EA App\n" +
    "• Running updates can be cancelled\n" +
    "• Downloading, installing and waiting for completion are shown separately\n" +
    "• New clear completion report with understandable error messages\n" +
    "• WinVora detects when Windows requires a restart after an update\n" +
    "• Activity history now includes program, version and result\n" +
    "• Windows notification after program updates finish\n" +
    "• System information can be copied by section\n" +
    "• Updates can be postponed for 1, 7 or 30 days or ignored permanently\n" +
    "  and restored together later\n" +
    "• New history page with result filters and text export\n" +
    "• Complete system report can be exported as a readable file\n" +
    "• New startup manager for safely enabling and disabling programs for the\n" +
    "  current Windows user\n" +
    "• Settings and changelog reliably open in front and remember size and position\n" +
    "• The loading screen stays smooth while moving and keeps its diagonal hex wave\n" +
    "• Internal components were cleaned up for more reliable future updates"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.8.1",
    "• Neue Suche: Verfügbare Programm-Updates lassen sich jetzt schnell nach\n" +
    "  Name oder Paket durchsuchen\n" +
    "• Suche zeigt Trefferzahlen und einen freundlichen Hinweis, wenn nichts passt\n" +
    "• Der Update-Button zeigt direkt, wie viele Programme ausgewählt sind\n" +
    "• Nach abgeschlossenen Programm-Updates wird die Liste automatisch erneuert\n" +
    "• Die Programmsuche beim Deinstallieren zeigt jetzt ebenfalls Trefferzahlen\n" +
    "• Programmgrößen werden deutlich häufiger angezeigt statt nur als N/A\n" +
    "• Herausgeber, Größe und Ladehinweise auf der Winget-Seite wechseln jetzt\n" +
    "  korrekt zwischen Deutsch und Englisch\n" +
    "• Der komplette WinVora-Updatebereich ist jetzt auch auf Englisch verfügbar\n" +
    "• Heruntergeladene WinVora-Updates werden vor der Installation geprüft\n" +
    "• Beschädigte oder unvollständige Downloads werden automatisch entfernt\n" +
    "• Die Bereinigung geschützter Windows-Dateien wurde sicherer gemacht\n" +
    "• Mehrfachklicks starten keine doppelten Lade- oder Bereinigungsvorgänge mehr\n" +
    "• WinVora merkt sich Größe und Position des Hauptfensters\n" +
    "• Neue Tastenkürzel: Strg+F für die Suche und Strg+R zum Aktualisieren\n" +
    "• Verbesserte Tastaturbedienung und Beschriftungen für Bildschirmleser\n" +
    "• Versionsanzeige in der App und im Installer bleibt automatisch gleich\n" +
    "• Fehler beim Laden oder Speichern von Einstellungen sind leichter zu finden",
    "• New search: quickly filter available program updates by name or package\n" +
    "• Search now shows result counts and a friendly message when nothing matches\n" +
    "• The update button directly shows how many programs are selected\n" +
    "• The list refreshes automatically after program updates finish\n" +
    "• Program search on the uninstall page now also shows result counts\n" +
    "• Program sizes are now shown much more often instead of only displaying N/A\n" +
    "• Publisher, size and loading text on the Winget page now switch correctly\n" +
    "  between German and English\n" +
    "• The complete WinVora update section is now available in English\n" +
    "• Downloaded WinVora updates are checked before installation\n" +
    "• Damaged or incomplete downloads are removed automatically\n" +
    "• Cleanup of protected Windows files is now safer\n" +
    "• Repeated clicks no longer start duplicate loading or cleanup operations\n" +
    "• WinVora remembers the main window size and position\n" +
    "• New shortcuts: Ctrl+F for search and Ctrl+R to refresh\n" +
    "• Improved keyboard navigation and screen reader labels\n" +
    "• Version numbers in the app and installer now always stay in sync\n" +
    "• Problems loading or saving settings are easier to diagnose"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.8.0",
    "• Update-Bereich in den Einstellungen jetzt ganz oben statt unten -\n" +
    "  zeigt sofort \"Jetzt aktualisieren\", falls schon eins gefunden wurde\n" +
    "• Einstellungen- und Changelog-Fenster kommen jetzt zuverlässig in\n" +
    "  den Vordergrund, statt manchmal hinter dem Hauptfenster zu bleiben\n" +
    "• Scrollbar-Abstand in Einstellungen/Changelog behoben\n" +
    "• GPU-Statuskarte in der oberen Reihe jetzt mit echten Werten\n" +
    "• Neue Mini-Verlaufsdiagramme für CPU/RAM/GPU (letzte 30 Werte),\n" +
    "  mit adaptiver Skalierung und aktuellem Wert direkt daneben\n" +
    "• Neuer Aktivitätsverlauf (Bereinigungen, Updates, Deinstallationen)\n" +
    "• Tooltips für alle Schnellzugriff-Buttons\n" +
    "• CPU/RAM/GPU zeigen jetzt sofort beim Start echte Werte, statt\n" +
    "  erst nach dem ersten Aktualisierungsintervall zu laden\n" +
    "• Aktualisierungsintervall-Einstellung erwähnt jetzt auch GPU\n" +
    "• Ladebildschirm komplett neu gestaltet: Hex-Grid-Muster mit\n" +
    "  durchlaufender Lila-Leuchtwelle, deckt jetzt zuverlässig den\n" +
    "  ganzen Bildschirm ab und ist mittig zentriert\n" +
    "• Ladebildschirm-Text im Hellmodus gefixt (war unsichtbar)\n" +
    "• Alle Karten und Kacheln auf einheitlichen Eckenradius vereinheitlicht\n" +
    "• Bugfix: Verlaufsdiagramme saßen wegen eines SettingsCard-\n" +
    "  Layout-Bugs immer nur schmal am rechten Rand statt in voller Breite",
    "• Update section in Settings now at the top instead of the bottom -\n" +
    "  shows \"Update Now\" immediately if one was already found\n" +
    "• Settings and Changelog windows now reliably come to the front\n" +
    "  instead of sometimes staying behind the main window\n" +
    "• Fixed scrollbar spacing in Settings/Changelog windows\n" +
    "• GPU status card in the top row now shows real values\n" +
    "• New mini history charts for CPU/RAM/GPU (last 30 values),\n" +
    "  with adaptive scaling and the current value shown right next to it\n" +
    "• New activity log (cleanups, updates, uninstalls)\n" +
    "• Tooltips for all Quick Access buttons\n" +
    "• CPU/RAM/GPU now show real values immediately at startup instead\n" +
    "  of only after the first update interval\n" +
    "• Update interval setting now also mentions GPU\n" +
    "• Loading screen completely redesigned: hex grid pattern with a\n" +
    "  flowing purple light wave, now reliably covers the whole screen\n" +
    "  and is centered\n" +
    "• Fixed loading screen text being invisible in light mode\n" +
    "• Unified corner radius across all cards and tiles\n" +
    "• Bugfix: history charts were stuck narrow on the right edge due to\n" +
    "  a SettingsCard layout quirk instead of using the full width"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.7.7",
    "• Storage- und Winget-Karten zeigen jetzt einen akzentfarbenen Rand,\n" +
    "  solange die Kategorie bzw. das Paket ausgewählt ist",
    "• Storage and Winget cards now show an accent-colored border\n" +
    "  while the category or package is selected"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.7.6",
    "• Fluent-Design-Konsistenz: Mica-Effekt jetzt auch in Einstellungen-\n" +
    "  und Changelog-Fenster sichtbar (Root-Hintergrund dafür leicht\n" +
    "  durchscheinend statt komplett opak)\n" +
    "• Weiche Schatten + Akzent-Hover jetzt auch auf Einstellungen-Karten,\n" +
    "  Changelog-Karten und Systeminfo-Karten (GPU/Laufwerke/Netzwerk)\n" +
    "• Akzentfarbe jetzt auf allen Fortschrittsbalken (Winget-Update,\n" +
    "  Storage-Bereinigung, App-Update)",
    "• Fluent Design consistency: Mica effect now also visible in the\n" +
    "  Settings and Changelog windows (root background slightly\n" +
    "  translucent instead of fully opaque)\n" +
    "• Soft shadows + accent hover now also on Settings cards,\n" +
    "  Changelog cards and System Info cards (GPU/drives/network)\n" +
    "• Accent color now on all progress bars (Winget update,\n" +
    "  storage cleanup, app update)"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.7.5",
    "• Bugfix: Systeminfo-Feldbezeichnungen wurden über FindName() gesucht,\n" +
    "  was nicht zuverlässig funktionierte - jetzt direkter Feldzugriff\n" +
    "• Alle 20 Storage-Kategorienamen und -Beschreibungen übersetzt\n" +
    "  (Benutzer Temp, Papierkorb, Prefetch, Windows Update Cache, etc.)",
    "• Bugfix: System Info field labels were looked up via FindName(),\n" +
    "  which wasn't reliable - now uses direct field access instead\n" +
    "• All 20 storage category names and descriptions translated\n" +
    "  (User Temp, Recycle Bin, Prefetch, Windows Update Cache, etc.)"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.7.4",
    "• Alle 26 Systeminfo-Feldbezeichnungen übersetzt (Computername,\n" +
    "  Hersteller/Modell, BIOS-Version, Secure Boot, TPM, etc.)\n" +
    "• GPU-/Laufwerks-/Netzwerk-Karten auf der Systeminfo-Seite übersetzt\n" +
    "• Deinstaller: \"installiert am\" und \"Deinstallieren\"-Button übersetzt\n" +
    "• Bugfix: doppelte Variablendeklaration verhinderte den Build",
    "• All 26 System Info field labels translated (Computer Name,\n" +
    "  Manufacturer/Model, BIOS Version, Secure Boot, TPM, etc.)\n" +
    "• GPU/drive/network cards on the System Info page translated\n" +
    "• Uninstaller: \"installed on\" and \"Uninstall\" button translated\n" +
    "• Bugfix: duplicate variable declaration prevented the build"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.7.3",
    "• Bugfix: Dashboard-Werte (Speicherplatz, Zuletzt bereinigt, Gesamtstatus)\n" +
    "  blieben beim Sprachwechsel in der ursprünglichen Sprache stehen -\n" +
    "  werden jetzt sofort neu berechnet\n" +
    "• \"Changelog anzeigen\"-Hinweistext in der Sidebar übersetzt\n" +
    "• Winget: \"Keine Updates gefunden\"-Meldung übersetzt",
    "• Bugfix: dashboard values (storage space, last cleaned, overall status)\n" +
    "  stayed in the original language after switching - now recalculated\n" +
    "  immediately\n" +
    "• \"View changelog\" hint text in the sidebar translated\n" +
    "• Winget: \"No updates found\" message translated"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.7.2",
    "• Weitere Lücken in der Übersetzung geschlossen: Speicherplatz-Anzeige,\n" +
    "  \"Zuletzt bereinigt\", Sicherheits-Status, Update-Zähler,\n" +
    "  alle Seiten-Untertitel (Winget/Storage/Deinstaller)",
    "• Closed further translation gaps: storage space display,\n" +
    "  \"Last cleaned\", security status, update counter,\n" +
    "  all page subtitles (Winget/Storage/Uninstall)"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.7.1",
    "• Sprachauswahl (Erststart + Einstellungen) jetzt als Dropdown\n" +
    "• Übersetzung deutlich erweitert: Systeminfo-Abschnittsüberschriften,\n" +
    "  Storage-Gruppennamen, Action-Bars (Winget/Storage/Deinstaller),\n" +
    "  Kontakt-Dialog\n" +
    "• Bugfix: Sprachauswahl-Dialog blockierte den App-Start komplett\n" +
    "  (schwarzer Bildschirm) - läuft jetzt erst nach dem normalen Laden",
    "• Language selection (first run + settings) is now a dropdown\n" +
    "• Translation significantly expanded: System Info section headers,\n" +
    "  Storage group names, action bars (Winget/Storage/Uninstall),\n" +
    "  Contact dialog\n" +
    "• Bugfix: language selection dialog completely blocked app startup\n" +
    "  (black screen) - now runs only after normal loading"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.7.0",
    "• Neu: Englische Sprachversion, umschaltbar in den Einstellungen\n" +
    "• Sprachauswahl erscheint einmalig beim allerersten Start\n" +
    "• Übersetzt: Sidebar, Dashboard, Schnellzugriff, Einstellungen-Fenster\n" +
    "• Tiefer liegende Bereiche (Systeminfo-Details, Storage/Winget/\n" +
    "  Deinstaller-Interna, Changelog-Einträge, Log-Meldungen) bleiben\n" +
    "  vorerst Deutsch",
    "• New: English language version, switchable in settings\n" +
    "• Language selection appears once on the very first start\n" +
    "• Translated: sidebar, dashboard, quick access, settings window\n" +
    "• Deeper areas (System Info details, Storage/Winget/Uninstall\n" +
    "  internals, changelog entries, log messages) remain German\n" +
    "  for now"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.6.2",
    "• \"Übersicht\" in der Sidebar heißt jetzt \"Dashboard\"\n" +
    "• Bugfix: Große Seiten-Überschrift zeigte teils den internen englischen\n" +
    "  Namen statt der deutschen Bezeichnung (z.B. \"Uninstall\" statt\n" +
    "  \"Deinstallieren\", \"Storage\" statt \"Dateien\") - jetzt überall konsistent\n" +
    "• Startseiten-Auswahl in den Einstellungen an Sidebar-Namen angeglichen",
    "• \"Übersicht\" in the sidebar is now called \"Dashboard\"\n" +
    "• Bugfix: the large page heading sometimes showed the internal English\n" +
    "  routing name instead of the proper label (e.g. \"Uninstall\" instead\n" +
    "  of \"Deinstallieren\", \"Storage\" instead of \"Dateien\") - now consistent\n" +
    "  everywhere\n" +
    "• Startup page selection in settings aligned with sidebar names"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.6.1",
    "• Weiche Schatten auf allen Dashboard-/Statuskarten (Übersicht)\n" +
    "• CPU-/RAM-Fortschrittsbalken auf der Systeminfo-Seite nutzen jetzt die Akzentfarbe\n" +
    "• Aktive Sidebar-Navigation wird jetzt farblich hervorgehoben",
    "• Soft shadows on all dashboard/status cards (overview)\n" +
    "• CPU/RAM progress bars on the System Info page now use the accent color\n" +
    "• The active sidebar navigation item is now highlighted"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.6.0",
    "• Übersichtsseite komplett überarbeitet: größere Statuskarten mit Icons\n" +
    "• Neues Live-Dashboard: Speicherplatz, installierte Programme,\n" +
    "  letzte Bereinigung, verfügbare Updates, Gesamtstatus\n" +
    "• GPU-Auslastung und CPU-/GPU-Temperatur jetzt über LibreHardwareMonitor\n" +
    "• Neue Akzentfarbe (Violett-Blau) für aktive Elemente und Hover-Effekte\n" +
    "• Schnellzugriff überarbeitet, jetzt mit Icons und Einstellungen-Button\n" +
    "• Dezente Hover-Animationen auf allen Dashboard-Karten",
    "• Overview page completely redesigned: bigger status cards with icons\n" +
    "• New live dashboard: storage space, installed programs, last cleanup,\n" +
    "  available updates, overall status\n" +
    "• GPU usage and CPU/GPU temperature now via LibreHardwareMonitor\n" +
    "• New accent color (violet-blue) for active elements and hover effects\n" +
    "• Quick access redesigned, now with icons and a settings button\n" +
    "• Subtle hover animations on all dashboard cards"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.5.0",
    "• Admin-pflichtige Bereiche auf der Dateien-Seite fragen jetzt gezielt\n" +
    "  per UAC-Prompt nach Rechten, statt stumm mit Fehler abzubrechen\n" +
    "• Sammel-Löschung bündelt alle Admin-Bereiche in einem einzigen UAC-Prompt\n" +
    "• Bugfix: Deadlock beim elevierten Löschvorgang behoben\n" +
    "• Bugfix: Windows Upgrade Logs ($WINDOWS.~BT) ließen sich wegen einer\n" +
    "  einzelnen geschützten Datei (Boot-Konfiguration) gar nicht löschen -\n" +
    "  jetzt wird Datei für Datei einzeln versucht statt alles-oder-nichts",
    "• Admin-required areas on the Files page now specifically request\n" +
    "  elevation via a UAC prompt instead of silently failing\n" +
    "• Bulk deletion now bundles all admin-required areas into a single UAC prompt\n" +
    "• Bugfix: fixed a deadlock in the elevated deletion process\n" +
    "• Bugfix: Windows Upgrade Logs ($WINDOWS.~BT) couldn't be deleted at all\n" +
    "  because of a single protected file (boot configuration) - now each\n" +
    "  file is attempted individually instead of all-or-nothing"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.4.9",
    "• Kontakt-Button in der Sidebar verkleinert\n" +
    "• Neuer Ko-fi-Button daneben zur Unterstützung von WinVora",
    "• Contact button in the sidebar made smaller\n" +
    "• New Ko-fi button next to it to support WinVora"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.4.8",
    "• Neuer Update-Hinweis: kleiner roter Badge am Einstellungen-Button,\n" +
    "  falls ein neues Update verfügbar ist\n" +
    "• Prüfung läuft still im Hintergrund beim App-Start, ohne zu stören",
    "• New update indicator: small red badge on the settings button if a\n" +
    "  new update is available\n" +
    "• Check runs quietly in the background at app startup without being intrusive"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.4.7",
    "• Publish-Größe von 1,9 GB auf 53 MB reduziert\n" +
    "  (PublishSingleFile verursachte einen Bündelungs-Bug mit der WindowsAppSDK)\n" +
    "• Ungenutzte KI/ML-Laufzeitkomponenten (ONNX Runtime u.a.) vom Build ausgeschlossen\n" +
    "• Update-Installation läuft jetzt komplett still (kein Assistenten-Fenster mehr)\n" +
    "• WinVora startet nach einem Update automatisch wieder\n" +
    "• Update-Bestätigungsdialog zeigt jetzt \"Jetzt aktualisieren\" statt \"Löschen\"",
    "• Publish size reduced from 1.9 GB to 53 MB\n" +
    "  (PublishSingleFile caused a bundling bug with the Windows App SDK)\n" +
    "• Unused AI/ML runtime components (ONNX Runtime etc.) excluded from the build\n" +
    "• Update installation now runs completely silently (no more wizard window)\n" +
    "• WinVora automatically restarts after an update\n" +
    "• Update confirmation dialog now shows \"Update Now\" instead of \"Delete\""
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.4.6",
    "• Update-Download: eindeutiger Temp-Dateiname pro Versuch\n" +
    "  (verhindert Konflikt mit noch laufendem Installer aus vorherigem Versuch)\n" +
    "• Update-Fortschritt zeigt jetzt immer heruntergeladene MB an,\n" +
    "  auch wenn der Server keine Gesamtgröße mitliefert\n" +
    "• Mehr Logging beim Update-Download für einfachere Fehlersuche",
    "• Update download: unique temp file name per attempt\n" +
    "  (prevents conflicts with a still-running installer from a previous attempt)\n" +
    "• Update progress now always shows downloaded MB, even if the server\n" +
    "  doesn't provide a total size\n" +
    "• More logging during update downloads for easier troubleshooting"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.4.5",
    "• Test-Release zur Überprüfung des Auto-Update-Mechanismus",
    "• Test release to verify the auto-update mechanism"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.4.4",
    "• Neu: Automatisches Update direkt aus den Einstellungen\n" +
    "  (prüft GitHub-Releases, lädt Installer herunter, aktualisiert automatisch)\n" +
    "• Ladebildschirm: Liquid-Glass-Bänder laufen jetzt wieder etwas ruhiger\n" +
    "• Bugfix: Bänder starteten fälschlicherweise alle mittig übereinander\n" +
    "• Cutouts der Glas-Bänder sind jetzt zufällig statt immer identisch",
    "• New: automatic update directly from settings\n" +
    "  (checks GitHub releases, downloads the installer, updates automatically)\n" +
    "• Loading screen: liquid glass bands now move a bit more calmly again\n" +
    "• Bugfix: bands incorrectly all started stacked in the center\n" +
    "• Cutouts in the glass bands are now random instead of always identical"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.4.3",
    "• Dateien-Seite zeigt jetzt \"Zuletzt bereinigt: vor X Tagen\" an\n" +
    "• Startbildschirm: Logo jetzt über dem \"WinVora\"-Schriftzug\n" +
    "• Startbildschirm: animierter Glas-Balken läuft im Hintergrund durch",
    "• Files page now shows \"Last cleaned: X days ago\"\n" +
    "• Loading screen: logo now sits above the \"WinVora\" wordmark\n" +
    "• Loading screen: animated glass bar runs through the background"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.4.2",
    "• Sidebar-Navigation: scrollbar, falls mehr Kategorien nicht mehr auf einmal reinpassen",
    "• Sidebar navigation: now scrollable if more categories don't fit at once"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.4.1",
    "• Kontakt-Seite mit echten Kontaktdaten aktualisiert\n" +
    "• Winget: Fix für durchgängiges \"N/A\" bei Herausgeber/Größe auf manchen PCs\n" +
    "  (älteres winget kannte ein verwendetes Flag nicht - jetzt entfernt)\n" +
    "• Fehler beim Abrufen von Winget-Details werden jetzt geloggt,\n" +
    "  damit sich sowas beim nächsten Mal leichter nachvollziehen lässt",
    "• Contact page updated with real contact details\n" +
    "• Winget: fixed persistent \"N/A\" for publisher/size on some PCs\n" +
    "  (older winget versions didn't recognize a flag we used - now removed)\n" +
    "• Errors while fetching Winget details are now logged, making it\n" +
    "  easier to diagnose next time"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.4.0",
    "• Neue Seite: Programme deinstallieren (Registry-Scan, Suche/Filter)\n" +
    "• Deinstallation startet den originalen Uninstaller jedes Programms\n" +
    "• Echte App-Icons statt Platzhalter-Symbolen bei Winget und Deinstaller\n" +
    "• Icons werden im Hintergrund nachgeladen, ohne die Liste zu blockieren\n" +
    "• Titelleisten-Fix: dünne Trennlinie liegt nicht mehr über dem Logo\n" +
    "• Richtiger Windows-Installer (Inno Setup) mit Sprachauswahl,\n" +
    "  wählbarem Installationsort und optionaler Desktop-Verknüpfung\n" +
    "• Installer erkennt vorhandene Installation automatisch und aktualisiert sie\n" +
    "• Installer schließt WinVora bei Bedarf automatisch vor einem Update\n" +
    "• Quellcode und Downloads jetzt in getrennten GitHub-Repos organisiert",
    "• New page: uninstall programs (registry scan, search/filter)\n" +
    "• Uninstalling launches each program's original uninstaller\n" +
    "• Real app icons instead of placeholder symbols for Winget and Uninstall\n" +
    "• Icons load in the background without blocking the list\n" +
    "• Title bar fix: thin divider line no longer overlaps the logo\n" +
    "• Proper Windows installer (Inno Setup) with language selection,\n" +
    "  choosable install location, and optional desktop shortcut\n" +
    "• Installer automatically detects an existing installation and updates it\n" +
    "• Installer automatically closes WinVora if needed before an update\n" +
    "• Source code and downloads now organized in separate GitHub repos"
));

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
    "• Einstellungs- und Changelog-Fenster: passende Größe + scrollbar",
    "• Files page: 20 categories sorted into 5 collapsible groups\n" +
    "• New setting: freely choosable startup page (Dashboard/System/Apps/Files)\n" +
    "• New setting: update interval for CPU/RAM (1/2/5 seconds)\n" +
    "• New setting: start with Windows (autostart)\n" +
    "• New setting: toggle confirmation before deleting\n" +
    "• New: open/clear the log file directly from settings\n" +
    "• New: reset settings with one click\n" +
    "• New contact button in the sidebar (below version)\n" +
    "• Glass intensity now fixed at 18 instead of adjustable\n" +
    "• Settings and changelog windows: proper size + scrollable"
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
    "• publish.bat: baut und zippt die Testversion automatisch",
    "• Project completely renamed to WinVora (namespace, exe, window title)\n" +
    "• Custom app icon for title bar and taskbar\n" +
    "• Thin divider line below the title bar for a cleaner top edge\n" +
    "• Glass cards now start below the window buttons instead of overlapping them\n" +
    "• Winget: download size now takes priority over install size\n" +
    "• Warning if Chrome/Edge are still running when clearing browser cache\n" +
    "• New logging (%LOCALAPPDATA%\\WinVora\\log.txt) for errors and actions\n" +
    "• Global error handler so silent crashes become traceable\n" +
    "• Self-contained single-file publish (no installation needed for testing)\n" +
    "• Admin manifest removed - app starts without a UAC prompt\n" +
    "• publish.bat: automatically builds and zips the test version"
));

            panel.Children.Add(MakeChangelogCard(
    "Version 0.3.1",
    "• Neu: Heller Modus (Umschalter in den Einstellungen)\n" +
    "• Einstellungen-Button jetzt über statt neben der Versions-Karte\n" +
    "• Winget-Liste läuft im Hintergrund - Oberfläche ruckelt beim Laden nicht mehr\n" +
    "• Refresh- und Start-Update-Button bei Winget einheitlich groß",
    "• New: light mode (toggle in settings)\n" +
    "• Settings button now above instead of next to the version card\n" +
    "• Winget list now loads in the background - the UI no longer stutters while loading\n" +
    "• Refresh and Start Update buttons on the Winget page are now a consistent size"
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
    "• Diverse Bugfixes (doppeltes Laden der Systeminfos behoben)",
    "• New Files page (storage cleanup) with 19 categories\n" +
    "• Selection via toggles, single and bulk deletion\n" +
    "• \"Select All\" button on the Files page\n" +
    "• Confirmation dialog before every deletion\n" +
    "• Progress display with live status while cleaning\n" +
    "• Winget: publisher and size are fetched automatically\n" +
    "• Winget: download progress in MB while installing\n" +
    "• Winget: clear error message if winget isn't installed\n" +
    "• App now starts automatically with administrator rights\n" +
    "• Custom dark title bar instead of the white system bar\n" +
    "• Background switched to pure black\n" +
    "• Cards now use a stronger liquid-glass white\n" +
    "• Real Mica backdrop with Acrylic fallback\n" +
    "• Hover effects on info cards\n" +
    "• Smooth fade-in when switching pages\n" +
    "• Loading screen at app startup\n" +
    "• Various bugfixes (fixed system info loading twice)"
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
    "• Winget-Prozesshandling verbessert",
    "• New overview as the startup page\n" +
    "• System Info, Winget, and Files as separate sections\n" +
    "• Large health cards for CPU, RAM, security, and updates\n" +
    "• Modernized liquid-glass interface\n" +
    "• Larger sidebar navigation\n" +
    "• New large System Info dropdowns\n" +
    "• All System Info categories are collapsible\n" +
    "• \"Expand All\" and \"Collapse All\" buttons\n" +
    "• System Info cards grouped by category\n" +
    "• Larger text, more spacing, better readability\n" +
    "• Changelog window in liquid-glass style\n" +
    "• Improved Winget process handling"
));

            panel.Children.Add(MakeChangelogCard(
                "Version 0.1.0",
                "• Schnellere Ladezeit\n" +
                "• CPU-Optimierung\n" +
                "• Live-Systeminfos\n" +
                "• Winget-Updateübersicht\n" +
                "• Erstes Changelog-Fenster",
                "• Faster load time\n" +
                "• CPU optimization\n" +
                "• Live system info\n" +
                "• Winget update overview\n" +
                "• First changelog window"
            ));

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(0, 0, 14, 0),
                Content = panel
            };

            var contentHost = new Grid { Padding = new Thickness(24, 16, 10, 24) };
            contentHost.Children.Add(scrollViewer);
            Grid.SetRow(contentHost, 1);

            var divider = MakeTitleBarDivider();
            Grid.SetRow(divider, 0);

            var titleLabel = MakeTitleBarLabel(Localization.T("Changelog.WindowTitle"));
            Grid.SetRow(titleLabel, 0);

            root.Children.Add(contentHost);
            root.Children.Add(divider);
            root.Children.Add(titleLabel);

            changelogWindow.Content = root;
            StyleDarkWindow(changelogWindow, _settings.ChangelogWindowWidth, _settings.ChangelogWindowHeight);
            WindowActivationService.PlaceWindow(this, changelogWindow,
                _settings.ChangelogWindowX, _settings.ChangelogWindowY,
                _settings.ChangelogWindowWidth, _settings.ChangelogWindowHeight);
            changelogWindow.Activate();
            WindowActivationService.ShowOwnedInFront(this, changelogWindow);
        }

        private Border MakeChangelogCard(string title, string textDe, string? textEn = null)
        {
            var text = Localization.CurrentLanguage == "en" && textEn != null ? textEn : textDe;

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

            var bulletList = new StackPanel { Spacing = 8 };
            var bulletItems = new List<string>();

            foreach (var rawLine in text.Replace("\r", "").Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.StartsWith("•", StringComparison.Ordinal))
                {
                    bulletItems.Add(line.TrimStart('•', ' '));
                }
                else if (!string.IsNullOrWhiteSpace(line) && bulletItems.Count > 0)
                {
                    bulletItems[^1] += " " + line;
                }
            }

            foreach (var item in bulletItems)
            {
                var row = new Grid { ColumnSpacing = 10 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var bullet = new TextBlock
                {
                    Text = "•",
                    FontSize = 15,
                    Foreground = (SolidColorBrush)RootGrid.Resources["AppAccentBrush"]
                };

                var body = new TextBlock
                {
                    Text = item,
                    FontSize = 14,
                    LineHeight = 21,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = (SolidColorBrush)RootGrid.Resources["AppForegroundCC"]
                };
                Grid.SetColumn(body, 1);

                row.Children.Add(bullet);
                row.Children.Add(body);
                bulletList.Children.Add(row);
            }

            content.Children.Add(bulletList);

            card.Child = content;
            AttachCardHoverEffect(card);
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

        private void SetupSystemInfoCopyButtons()
        {
            string FromSnapshot(Func<SystemInfoSnapshot, string> format) =>
                _cachedSnapshot == null ? "" : format(_cachedSnapshot);

            SystemInfoCopyButton.Attach(SysCardDevice,
                () => FromSnapshot(SystemInfoFormatter.Device));
            SystemInfoCopyButton.Attach(SysCardOs,
                () => FromSnapshot(SystemInfoFormatter.OperatingSystem));
            SystemInfoCopyButton.Attach(SysCardCpu,
                () => FromSnapshot(snapshot => SystemInfoFormatter.Cpu(snapshot, Localization.CurrentLanguage == "en")));
            SystemInfoCopyButton.Attach(SysCardRam,
                () => FromSnapshot(snapshot => SystemInfoFormatter.Ram(snapshot, Localization.CurrentLanguage == "en")));
            SystemInfoCopyButton.Attach(SysCardBoard,
                () => FromSnapshot(SystemInfoFormatter.Board));
            SystemInfoCopyButton.Attach(SysCardSecurity,
                () => FromSnapshot(SystemInfoFormatter.Security));
            SystemInfoCopyButton.Attach(SysCardGpu,
                () => FromSnapshot(SystemInfoFormatter.Gpus));
            SystemInfoCopyButton.Attach(SysCardDrives,
                () => FromSnapshot(SystemInfoFormatter.Drives));
            SystemInfoCopyButton.Attach(SysCardNetwork,
                () => FromSnapshot(SystemInfoFormatter.Network));
            SystemInfoCopyButton.Attach(SysCardBattery,
                () => FromSnapshot(SystemInfoFormatter.Battery));

            foreach (var value in new[]
            {
                SysComputerName, SysUserName, SysManufacturerModel, SysSerialNumber, SysArchitecture,
                SysEdition, SysVersionBuild, SysInstallDate, SysLastUpdate, SysActivation, SysUptime,
                SysDotNet, SysDirectX, SysCpuName, SysCpuDetails, SysRamDetails, SysMainboard, SysBios,
                SysSecureBoot, SysTpm, SysVirtualization, SysDefender, SysFirewall, SysBitLocker, SysBattery
            })
            {
                value.MaxWidth = 620;
                value.Margin = new Thickness(0, 0, 18, 0);
            }

        }

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
            SysCpuDetails.Text = Localization.CurrentLanguage == "en"
                ? $"{s.CpuCores} Cores / {s.CpuThreads} Threads / {s.CpuClock}"
                : $"{s.CpuCores} Kerne / {s.CpuThreads} Threads / {s.CpuClock}";

            SysRamDetails.Text = Localization.CurrentLanguage == "en"
                ? $"{s.RamTotal} installed, {s.RamUsed} used, {s.RamFree} free"
                : $"{s.RamTotal} installiert, {s.RamUsed} belegt, {s.RamFree} frei";

            SysMainboard.Text = s.Mainboard;
            SysBios.Text = s.BiosVersion;

            SysSecureBoot.Text = s.SecureBoot;
            SysTpm.Text = s.TpmVersion;
            SysVirtualization.Text = s.Virtualization;
            SysDefender.Text = s.DefenderStatus;
            SysFirewall.Text = s.FirewallStatus;
            SysBitLocker.Text = s.BitLockerStatus;

            bool en = Localization.CurrentLanguage == "en";

            SysGpuPanel.Children.Clear();
            if (s.Gpus.Length == 0)
            {
                SysGpuPanel.Children.Add(MakeInfoCard(en ? "No GPU detected" : "Keine GPU erkannt", ""));
            }
            foreach (var gpu in s.Gpus)
            {
                SysGpuPanel.Children.Add(MakeInfoCard(gpu, en ? "Graphics Card" : "Grafikkarte"));
            }

            SysDrivesPanel.Children.Clear();
            foreach (var drive in s.Drives)
            {
                SysDrivesPanel.Children.Add(MakeInfoCard(drive.Name, drive.TotalSize,
                    en ? $"{drive.FreeSpace} free" : $"{drive.FreeSpace} frei"));
            }

            SysNetworkPanel.Children.Clear();
            if (s.NetworkAdapters.Length == 0)
            {
                SysNetworkPanel.Children.Add(MakeInfoCard(en ? "No active network adapter found" : "Kein aktiver Netzwerkadapter gefunden", ""));
            }
            foreach (var net in s.NetworkAdapters)
            {
                SysNetworkPanel.Children.Add(MakeInfoCard(
                    net.Name,
                    $"IPv4: {net.IPv4}  •  MAC: {net.MacAddress}",
                    en ? $"Gateway: {net.Gateway}\nDNS: {net.Dns}" : $"Gateway: {net.Gateway}\nDNS: {net.Dns}"));
            }

            SysBattery.Text = s.BatteryStatus;
            var defenderOk = s.DefenderStatus.Contains("Aktiv", StringComparison.OrdinalIgnoreCase) ||
                              s.DefenderStatus.Contains("Active", StringComparison.OrdinalIgnoreCase);
            var firewallOk = s.FirewallStatus.Contains("Aktiv", StringComparison.OrdinalIgnoreCase) ||
                              s.FirewallStatus.Contains("Active", StringComparison.OrdinalIgnoreCase);

            HealthSecurityText.Text = (defenderOk, firewallOk) switch
            {
                (true, true) => en ? "Active" : "Aktiv",
                (false, true) => en ? "Check Defender" : "Defender prüfen",
                (true, false) => en ? "Check Firewall" : "Firewall prüfen",
                _ => en ? "Check" : "Prüfen"
            };
        }

        // Kleine Hilfsmethode, um schnell eine SettingsCard mit Header/Beschreibung/Inhalt zu bauen
        private Border MakeInfoCard(
            string header,
            string description,
            string? content = null,
            SolidColorBrush? statusBorder = null)
        {
            var item = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MinHeight = 105,
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(22),
                Background = (SolidColorBrush)RootGrid.Resources["AppOverlay18"],
                BorderBrush = statusBorder ?? (SolidColorBrush)RootGrid.Resources["AppOverlay28"],
                BorderThickness = new Thickness(1)
            };

            // Bestehender Hintergrund-Hover, plus Akzentfarbe am Rand beim
            // Überfahren (konsistent mit den Dashboard-/Settings-Karten).
            var infoCardOriginalBorder = item.BorderBrush;
            item.PointerEntered += (_, __) =>
            {
                item.Background = (SolidColorBrush)RootGrid.Resources["AppOverlay28"];
                if (statusBorder == null)
                    item.BorderBrush = (SolidColorBrush)RootGrid.Resources["AppAccentBrushLight"];
            };
            item.PointerExited += (_, __) =>
            {
                item.Background = (SolidColorBrush)RootGrid.Resources["AppOverlay18"];
                item.BorderBrush = infoCardOriginalBorder;
            };

            item.Shadow = new ThemeShadow();
            item.Translation = new System.Numerics.Vector3(0, 0, 12);

            var panel = new StackPanel
            {
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            var headerGrid = new Grid { ColumnSpacing = 12 };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var headerText = new TextBlock
            {
                Text = header,
                FontSize = 17,
                Foreground = (SolidColorBrush)RootGrid.Resources["AppForegroundBrush"],
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            };
            var copyButton = SystemInfoCopyButton.Create(
                () => SystemInfoFormatter.Card(header, description, content));
            Grid.SetColumn(copyButton, 1);
            headerGrid.Children.Add(headerText);
            headerGrid.Children.Add(copyButton);
            panel.Children.Add(headerGrid);

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

        private Border MakeEmptyState(
            string glyph,
            string title,
            string description,
            string? actionText = null,
            Func<Task>? action = null)
        {
            var panel = new StackPanel
            {
                Spacing = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(20)
            };
            panel.Children.Add(new FontIcon
            {
                Glyph = glyph,
                FontSize = 30,
                Foreground = (SolidColorBrush)RootGrid.Resources["AppAccentBrushLight"]
            });
            panel.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 18,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            panel.Children.Add(new TextBlock
            {
                Text = description,
                Foreground = (SolidColorBrush)RootGrid.Resources["AppFaintForegroundBrush"],
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            });
            if (action != null && !string.IsNullOrWhiteSpace(actionText))
            {
                var button = new Button { Content = actionText, HorizontalAlignment = HorizontalAlignment.Center };
                button.Click += async (_, __) => await action();
                panel.Children.Add(button);
            }
            return new Border
            {
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(20),
                Background = (SolidColorBrush)RootGrid.Resources["AppOverlay10"],
                BorderBrush = (SolidColorBrush)RootGrid.Resources["AppOverlay22"],
                BorderThickness = new Thickness(1),
                Child = panel
            };
        }



        private int _hardwareTickCounter;
        private readonly Queue<double> _cpuHistory = new();
        private readonly Queue<double> _ramHistory = new();
        private readonly Queue<double> _gpuHistory = new();
        private const int HistoryMaxPoints = 30;

        private void StartLiveUsageTimer()
        {
            _liveUsageTimer?.Stop();
            _hardwareTickCounter = 0;
            _liveUsageTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(_settings.LiveUpdateIntervalSeconds) };
            _liveUsageTimer.Tick += async (_, __) => await UpdateLiveUsageAsync();

            _liveUsageTimer.Start();

            // BUGFIX: Vorher stand überall "--%", bis das erste Timer-Intervall
            // verstrichen war (Standard 2 Sekunden, bei größerem Intervall
            // entsprechend länger) - jetzt wird sofort einmal aktualisiert,
            // statt auf den ersten Tick zu warten. Die GPU-Drosselung (nur
            // jeder 3. Tick) wird für diesen allerersten Aufruf bewusst
            // übersprungen, sonst würde GPU trotzdem erst nach 2-3 Intervallen
            // erscheinen.
            _ = UpdateLiveUsageAsync(forceHardwareRead: true);
        }

        private async Task UpdateLiveUsageAsync(bool forceHardwareRead = false)
        {
            // Läuft im Hintergrund, damit der UI-Thread (und damit das
            // Scrollen) nicht alle 2 Sekunden kurz blockiert wird.
            var (cpu, ram, _, ramUsedGb, ramTotalGb) = await Task.Run(() => SystemInfoProvider.GetLiveUsage());

            SysCpuUsageBar.Value = cpu;
            SysCpuUsageText.Text = $"{cpu}%";

            SysRamUsageBar.Value = ram;
            SysRamUsageText.Text = $"{ram}%";

            HealthCpuText.Text = $"{cpu}%";
            HealthRamText.Text = $"{ram}%";

            UpdateHistoryChart(CpuHistoryLine, CpuHistoryCanvas, _cpuHistory, cpu, CpuHistoryCurrentText);
            UpdateHistoryChart(RamHistoryLine, RamHistoryCanvas, _ramHistory, ram, RamHistoryCurrentText);

            if (ramTotalGb > 0)
            {
                var ramDetail = $"{ramUsedGb:0.0} / {ramTotalGb:0.0} GB";
                HealthRamDetailText.Text = ramDetail;
                DashRamDetailText.Text = ramDetail;
            }

            // GPU-Auslastung/Temperatur sind über LibreHardwareMonitor
            // deutlich "teurer" abzufragen als die einfachen Performance
            // Counter für CPU/RAM - deshalb bewusst nur jeden 3. Tick,
            // um nicht unnötig Ressourcen zu verbrauchen.
            _hardwareTickCounter++;
            if (forceHardwareRead || _hardwareTickCounter % 3 == 0)
            {
                var readings = await Task.Run(() => HardwareMonitorService.GetReadings());

                // Große Statuskarte oben (StatCardGpu) befüllen - die kleine
                // GPU-Kachel im Live-Dashboard wurde entfernt, da GPU jetzt
                // schon oben und im Verlaufsdiagramm sichtbar ist.
                HealthGpuText.Text = readings.GpuLoadPercent != null
                    ? $"{readings.GpuLoadPercent:0}%"
                    : "N/A";

                if (readings.GpuLoadPercent != null)
                    UpdateHistoryChart(GpuHistoryLine, GpuHistoryCanvas, _gpuHistory, readings.GpuLoadPercent.Value, GpuHistoryCurrentText);

                string tempText;
                if (readings.CpuTemperature != null && readings.GpuTemperature != null)
                    tempText = $"CPU {readings.CpuTemperature:0}° / GPU {readings.GpuTemperature:0}°";
                else if (readings.CpuTemperature != null)
                    tempText = $"CPU {readings.CpuTemperature:0}°";
                else if (readings.GpuTemperature != null)
                    tempText = $"GPU {readings.GpuTemperature:0}°";
                else
                    tempText = "Nicht verfügbar";

                DashTempText.Text = tempText;
                DashTempText.Foreground = (readings.CpuTemperature != null || readings.GpuTemperature != null)
                    ? (SolidColorBrush)RootGrid.Resources["AppForegroundBrush"]
                    : (SolidColorBrush)RootGrid.Resources["AppFaintForegroundBrush"];
            }
        }

        // Befüllt die zusätzlichen Live-Dashboard-Kacheln auf der Übersicht
        // (Speicherplatz, installierte Programme, letzte Bereinigung,
        // Updates-Anzahl, Gesamtstatus). Läuft einmalig nach dem Start und
        // wird danach nicht automatisch wiederholt (die Werte ändern sich
        // selten genug, dass ein manuelles "Refresh" auf den jeweiligen
        // Seiten ausreicht).
        private async Task PopulateDashboardWidgetsAsync()
        {
            // Speicherplatz - erstes Laufwerk aus dem bereits geladenen Snapshot
            var firstDrive = _cachedSnapshot?.Drives?.FirstOrDefault();
            DashDiskText.Text = firstDrive != null
                ? Localization.CurrentLanguage == "en"
                    ? $"{firstDrive.FreeSpace} free of {firstDrive.TotalSize}"
                    : $"{firstDrive.FreeSpace} frei von {firstDrive.TotalSize}"
                : Localization.T("Dash.NotAvailable");

            // Zuletzt bereinigt
            DashLastCleanupText.Text = FormatLastCleanup(_settings.LastCleanupUtc);

            // Installierte Programme (Registry-Scan - im Hintergrund, kann kurz dauern)
            try
            {
                var count = await Task.Run(() => InstalledProgramsService.GetInstalledPrograms().Count);
                DashInstalledCountText.Text = count.ToString();
            }
            catch (Exception ex)
            {
                Logger.LogError("PopulateDashboardWidgetsAsync (Programme)", ex);
                DashInstalledCountText.Text = "N/A";
            }

            UpdateDashboardStatusSummary();
        }

        // Aktualisiert ein minimalistisches Verlaufsdiagramm (wie ein kleines
        // Windows-Widget): fügt den neuen Wert hinzu, verwirft alte Werte über
        // dem Limit, und zeichnet die Punkte als einfache Linie neu.
        //
        // BUGFIX: Vorher wurde immer fest auf 0-100% skaliert - bei normaler
        // Auslastung (z.B. 5-20%) sah die Linie dadurch fast wie eine flache
        // Gerade am unteren Rand aus, man konnte Schwankungen kaum erkennen.
        // Jetzt wird adaptiv auf den tatsächlichen Min/Max-Bereich der
        // sichtbaren Werte skaliert (mit etwas Puffer oben/unten), damit auch
        // kleine Ausschläge gut sichtbar sind.
        private void UpdateHistoryChart(Polyline line, Canvas canvas, Queue<double> history, double newValue, TextBlock? currentValueText = null)
        {
            history.Enqueue(Math.Clamp(newValue, 0, 100));
            while (history.Count > HistoryMaxPoints)
                history.Dequeue();

            if (currentValueText != null)
                currentValueText.Text = $"{newValue:0}%";

            if (history.Count < 2 || canvas.ActualWidth <= 0) return;

            var values = history.ToArray();
            double stepX = canvas.ActualWidth / (HistoryMaxPoints - 1);
            double height = canvas.ActualHeight > 0 ? canvas.ActualHeight : 90;

            double min = values.Min();
            double max = values.Max();

            // Mindestens 10 Prozentpunkte Spannweite, sonst wirkt eine fast
            // konstante Auslastung (z.B. immer genau 4%) optisch zu nervös.
            double range = Math.Max(max - min, 10);
            double padding = range * 0.15;
            double scaleMin = Math.Max(0, min - padding);
            double scaleMax = Math.Min(100, max + padding);
            double scaleRange = Math.Max(scaleMax - scaleMin, 1);

            var points = new PointCollection();

            // Falls noch nicht genug Werte gesammelt wurden, rechts ausgerichtet
            // zeichnen (neueste Werte immer am rechten Rand).
            int offset = HistoryMaxPoints - values.Length;

            for (int i = 0; i < values.Length; i++)
            {
                double x = (offset + i) * stepX;
                double normalized = (values[i] - scaleMin) / scaleRange;
                double y = height - (normalized * height);
                points.Add(new Windows.Foundation.Point(x, y));
            }

            line.Points = points;
        }


        // Wird nach dem initialen Laden UND jedes Mal aufgerufen, wenn sich
        // die Winget-Paketliste ändert (Refresh auf der Apps-Seite).
        private void UpdateDashboardStatusSummary()
        {
            bool en = Localization.CurrentLanguage == "en";
            int updateCount = _cachedPackages?.Count ?? 0;
            DashUpdatesCountText.Text = updateCount == 0 ? (en ? "None" : "Keine") : updateCount.ToString();

            bool securityOk = HealthSecurityText.Text.Contains("Aktiv", StringComparison.OrdinalIgnoreCase) ||
                               HealthSecurityText.Text.Contains("Active", StringComparison.OrdinalIgnoreCase) ||
                               HealthSecurityText.Text.Contains("OK", StringComparison.OrdinalIgnoreCase);

            if (updateCount == 0 && securityOk)
            {
                DashOverallStatusText.Text = Localization.T("Dash.AllUpToDate");
            }
            else if (updateCount > 0)
            {
                DashOverallStatusText.Text = en ? $"{updateCount} update(s) available" : $"{updateCount} Update(s) verfügbar";
            }
            else
            {
                DashOverallStatusText.Text = Localization.T("Dash.PleaseCheck");
            }
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
            var visibleRows = _wingetRows.Where(r => r.Card.Visibility == Visibility.Visible).ToList();
            if (visibleRows.Count == 0) return;

            bool newState = WingetSelectAllButton.IsChecked == true;

            foreach (var row in visibleRows)
                row.Toggle.IsOn = newState;

            WingetSelectAllButton.Content = Localization.T("Common.SelectAll");
            UpdateWingetSelectionButton();
        }

        private void UpdateWingetSelectionButton()
        {
            int count = _wingetRows.Count(row => row.Toggle.IsOn);
            bool en = Localization.CurrentLanguage == "en";
            StartUpdateButton.Content = count == 1
                ? (en ? "Install 1 update" : "1 Update installieren")
                : (en ? $"Install {count} updates" : $"{count} Updates installieren");
            StartUpdateButton.IsEnabled = count > 0 && !_isLoadingWinget && !_isUpdatingWinget;
        }

        private void WingetSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var query = WingetSearchBox.Text?.Trim() ?? "";

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
                    ContentArea.Children.Add(new TextBlock
                    {
                        Text = en ? $"Error running winget: {errorMessage}" : $"Fehler beim Ausführen von winget: {errorMessage}",
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

            bool en = Localization.CurrentLanguage == "en";
            DateTime now = DateTime.UtcNow;
            _settings.DeferredUpdates.RemoveAll(entry => entry.HiddenUntilUtc.HasValue && entry.HiddenUntilUtc <= now);
            var allPackages = packages.ToList();
            var hiddenIds = _settings.DeferredUpdates.Select(entry => entry.PackageId).ToHashSet(StringComparer.OrdinalIgnoreCase);
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
            }

            if (packages.Count == 0)
            {
                PageSubtitle.Text = en ? "No updates available" : "Keine Updates verfügbar";
                HealthUpdatesText.Text = Localization.T("Common.None");
                WingetSelectAllButton.Content = Localization.T("Common.SelectAll");
                WingetSelectAllButton.IsChecked = false;

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
            WingetSelectAllButton.Content = Localization.T("Common.SelectAll");
            WingetSelectAllButton.IsChecked = true;

            foreach (var pkg in packages)
            {
                var toggle = new ToggleSwitch { IsOn = true, OnContent = "", OffContent = "" };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle,
                    en ? $"Select update for {pkg.Name}" : $"Update für {pkg.Name} auswählen");
                var baseDescription = en
                    ? $"VERSION   {pkg.Version}  →  {pkg.Available}\nPACKAGE   {pkg.Id}  ·  {pkg.Source}"
                    : $"VERSION   {pkg.Version}  →  {pkg.Available}\nPAKET   {pkg.Id}  ·  {pkg.Source}";

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
                cardActions.Children.Add(deferButton);

                var card = new ToolkitControls.SettingsCard
                {
                    Header = pkg.Name,
                    Description = $"{baseDescription}\n{publisherLabel.ToUpperInvariant()}   {loadingLabel}     {sizeLabel.ToUpperInvariant()}   {loadingLabel}",
                    HeaderIcon = new FontIcon { Glyph = "\uE7B8" }, // Platzhalter-App-Icon
                    Content = cardActions,
                    BorderThickness = new Thickness(1),
                    BorderBrush = (SolidColorBrush)RootGrid.Resources["AppAccentBrushLight"] // startet ausgewählt
                };
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
            _settings.DeferredUpdates.Add(new DeferredUpdateEntry
            {
                PackageId = package.Id,
                HiddenUntilUtc = days.HasValue ? DateTime.UtcNow.AddDays(days.Value) : null
            });
            _settings.Save();
            if (_cachedPackages != null) RenderWingetPackages(_cachedPackages);
            ShowInfo(days.HasValue
                ? $"{package.Name} wurde für {days.Value} Tag(e) zurückgestellt."
                : $"{package.Name} wird dauerhaft ignoriert.");
        }

        private async Task LoadWingetIconsInBackground(
            List<(WingetPackage Package, ToggleSwitch Toggle, ToolkitControls.SettingsCard Card, string BaseDescription)> rows)
        {
            try
            {
                var installedPrograms = await Task.Run(() => InstalledProgramsService.GetInstalledPrograms());

                foreach (var row in rows)
                {
                    var iconPath = InstalledProgramsService.FindIconPathForName(installedPrograms, row.Package.Name);
                    if (string.IsNullOrWhiteSpace(iconPath)) continue;

                    await LoadCardIconAsync(row.Card, iconPath);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("LoadWingetIconsInBackground", ex);
            }
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
            var installedPrograms = await Task.Run(() => InstalledProgramsService.GetInstalledPrograms());
            var publisherLabel = Localization.T("Winget.Publisher");
            var sizeLabel = Localization.T("Winget.Size");

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
                        var (publisher, size) = await GetWingetDetailsAsync(
                            row.Package.Id, row.Package.Name, installedPrograms);
                        pending.Enqueue((row.Card,
                            $"{row.BaseDescription}\n{publisherLabel.ToUpperInvariant()}   {publisher}     {sizeLabel.ToUpperInvariant()}   {size}"));
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
        private async Task<(string Publisher, string Size)> GetWingetDetailsAsync(
            string packageId, string packageName, List<InstalledProgram> installedPrograms)
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
                // Hinweis: "--disable-interactivity" bewusst NICHT gesetzt - ältere
                // winget-Versionen kennen dieses Flag nicht und brechen dann den
                // kompletten Befehl mit einem Fehler ab, was zu durchgängigem
                // "N/A" bei Herausgeber/Größe führt (auch wenn winget selbst
                // grundsätzlich funktioniert).

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

                // Fehlerausgabe jetzt mitschreiben statt zu verwerfen, damit man bei
                // durchgängigem "N/A" im Log nachvollziehen kann, woran es lag.
                var errorOutput = new StringBuilder();
                var errorTask = Task.Run(async () =>
                {
                    while (!p.StandardError.EndOfStream)
                    {
                        var line = await p.StandardError.ReadLineAsync();
                        if (!string.IsNullOrWhiteSpace(line))
                            errorOutput.AppendLine(line);
                    }
                });

                await Task.WhenAll(outputTask, errorTask, p.WaitForExitAsync());

                if (publisher == "N/A" && size == "N/A")
                {
                    var errText = errorOutput.ToString().Trim();
                    Logger.Log($"winget show '{packageId}' lieferte weder Herausgeber noch Größe " +
                               $"(ExitCode {p.ExitCode}){(string.IsNullOrEmpty(errText) ? "" : $": {errText}")}");
                }

                var registryDetails = InstalledProgramsService.FindDetailsForPackage(
                    installedPrograms, packageName, packageId);

                if (publisher == "N/A" && !string.IsNullOrWhiteSpace(registryDetails.Publisher))
                    publisher = registryDetails.Publisher;

                if (size == "N/A" && !string.IsNullOrWhiteSpace(registryDetails.Size))
                    size = registryDetails.Size;
            }
            catch (Exception ex)
            {
                Logger.LogError($"GetWingetDetailsAsync({packageId})", ex);
            }

            bool en = Localization.CurrentLanguage == "en";
            return (publisher == "N/A" ? (en ? "Unknown" : "Unbekannt") : publisher,
                    size == "N/A" ? (en ? "Unknown" : "Unbekannt") : size);
        }

        private async void StartUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingWinget || _isLoadingWinget) return;
            _isUpdatingWinget = true;
            var selected = _wingetRows.Where(r => r.Toggle.IsOn).Select(r => r.Package).ToList();

            if (selected.Count == 0)
            {
                UpdateProgressPanel.Visibility = Visibility.Visible;
                UpdateProgressText.Text = "Keine Pakete ausgewählt.";
                UpdateProgressBar.Value = 0;
                _isUpdatingWinget = false;
                UpdateWingetSelectionButton();
                return;
            }

            bool en = Localization.CurrentLanguage == "en";
            bool containsEaApp = selected.Any(package =>
                package.Id.Equals("ElectronicArts.EADesktop", StringComparison.OrdinalIgnoreCase));
            var confirmation = new ContentDialog
            {
                XamlRoot = RootGrid.XamlRoot,
                Title = en ? "Install selected updates?" : "Ausgewählte Updates installieren?",
                Content = containsEaApp
                    ? (en
                        ? "The EA app is selected. Its installer previously restarted this PC without warning. WinVora will now open installers visibly, but a publisher installer may still request or initiate a restart. Save your work before continuing."
                        : "Die EA App ist ausgewählt. Ihr Installer hat diesen PC bereits ohne Warnung neu gestartet. WinVora öffnet Installer jetzt sichtbar, trotzdem kann ein Hersteller-Installer einen Neustart anfordern oder auslösen. Speichere vor dem Fortfahren deine Arbeit.")
                    : (en
                        ? "Publisher installers will be shown visibly. Some installers may request a restart. Save your work before continuing."
                        : "Die Installer der Hersteller werden sichtbar geöffnet. Einige Installer können einen Neustart verlangen. Speichere vor dem Fortfahren deine Arbeit."),
                PrimaryButtonText = en ? "Install" : "Installieren",
                CloseButtonText = en ? "Cancel" : "Abbrechen",
                DefaultButton = ContentDialogButton.Close
            };

            if (await confirmation.ShowAsync() != ContentDialogResult.Primary)
            {
                _isUpdatingWinget = false;
                UpdateWingetSelectionButton();
                return;
            }

            RefreshButton.IsEnabled = false;
            StartUpdateButton.IsEnabled = false;
            CancelUpdateButton.Visibility = Visibility.Visible;
            CancelUpdateButton.IsEnabled = true;
            _wingetUpdateCancellation = new CancellationTokenSource();

            UpdateProgressPanel.Visibility = Visibility.Visible;
            UpdateProgressBar.Maximum = selected.Count;
            UpdateProgressBar.Value = 0;

            var results = new List<(WingetPackage Package, WingetUpdateResult Result)>();

            var progress = new Progress<WingetUpdateProgress>(p =>
            {
                string phaseText = p.Phase switch
                {
                    WingetUpdatePhase.Downloading => en ? "Downloading" : "Wird heruntergeladen",
                    WingetUpdatePhase.Installing => en ? "Installer is running" : "Installer läuft",
                    _ => en ? "Waiting for completion" : "Warte auf Abschluss"
                };
                CurrentPackageStatusText.Text = string.IsNullOrWhiteSpace(p.Text)
                    ? phaseText
                    : $"{phaseText} · {p.Text}";

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
                SetGlobalStatus(Localization.CurrentLanguage == "en"
                    ? $"Updating {pkg.Name}..."
                    : $"{pkg.Name} wird aktualisiert...");
                UpdateProgressText.Text = $"Installiere {pkg.Name} ({i + 1}/{selected.Count})...";
                CurrentPackageStatusText.Text = "";
                CurrentPackageProgressBar.IsIndeterminate = true;
                CurrentPackageProgressBar.Value = 0;

                if (_wingetUpdateCancellation.IsCancellationRequested)
                    break;

                Logger.Log($"Programm-Update gestartet: {pkg.Name} [{pkg.Id}] {pkg.Version} -> {pkg.Available}");
                bool pendingRestartBefore = RestartDetectionService.IsRestartPending();
                var result = await _wingetUpdateService.UpgradeAsync(pkg.Id, progress, _wingetUpdateCancellation.Token);
                bool pendingRestartAfter = RestartDetectionService.IsRestartPending();
                if (!pendingRestartBefore && pendingRestartAfter && result.Status == WingetUpdateStatus.Successful)
                    result = result with
                    {
                        Status = WingetUpdateStatus.RestartRequired,
                        RestartRequired = true,
                        Message = en ? "Installed; Windows reports that a restart is required." : "Installiert; Windows meldet einen erforderlichen Neustart."
                    };

                results.Add((pkg, result));
                LogWingetUpdateActivity(pkg, result);
                Logger.Log($"Programm-Update beendet: {pkg.Name} [{pkg.Id}], Status={result.Status}, " +
                           $"ExitCode=0x{unchecked((uint)result.ExitCode):X8}, Meldung={result.Message}");

                CurrentPackageProgressBar.IsIndeterminate = false;
                CurrentPackageProgressBar.Value = 100;
                UpdateProgressBar.Value = i + 1;
            }

            bool cancelled = _wingetUpdateCancellation.IsCancellationRequested;
            int successCount = results.Count(item => item.Result.Status == WingetUpdateStatus.Successful);
            int failedCount = results.Count(item => item.Result.Status == WingetUpdateStatus.Failed);
            int cancelledCount = results.Count(item => item.Result.Status == WingetUpdateStatus.Cancelled) +
                                 Math.Max(0, selected.Count - results.Count);
            int restartCount = results.Count(item => item.Result.RestartRequired);
            UpdateProgressText.Text = cancelled
                ? (en ? "Update process cancelled." : "Updatevorgang abgebrochen.")
                : failedCount == 0
                    ? (en ? "All selected updates were installed." : "Alle ausgewählten Updates wurden installiert.")
                    : (en ? $"Finished with {failedCount} error(s)." : $"Mit {failedCount} Fehler(n) beendet.");
            CurrentPackageStatusText.Text = "";

            if (successCount > 0)
            {
                LogActivity("\uE895",
                    $"{successCount} Programm(e) aktualisiert",
                    $"{successCount} program(s) updated");
            }

            NotificationService.ShowUpdateSummary(successCount, failedCount, cancelledCount, restartCount);

            await ShowUpdateSummaryAsync(results, selected.Count - results.Count);

            // Kurz die Abschlussmeldung stehen lassen, dann automatisch neu laden
            await Task.Delay(2000);
            UpdateProgressPanel.Visibility = Visibility.Collapsed;
            CancelUpdateButton.Visibility = Visibility.Collapsed;
            _wingetUpdateCancellation.Dispose();
            _wingetUpdateCancellation = null;
            SetGlobalStatus(null);

            // Nach einer Installation ist der Cache veraltet - erzwungener Reload.
            _cachedPackages = null;
            _isUpdatingWinget = false;
            await LoadWinget(forceRefresh: true);
        }

        private void CancelUpdate_Click(object sender, RoutedEventArgs e)
        {
            CancelUpdateButton.IsEnabled = false;
            CurrentPackageStatusText.Text = Localization.CurrentLanguage == "en"
                ? "Cancelling current installer..."
                : "Aktueller Installer wird abgebrochen...";
            _wingetUpdateCancellation?.Cancel();
            Logger.Log("Programm-Update wurde vom Benutzer abgebrochen.");
        }

        private void LogWingetUpdateActivity(WingetPackage package, WingetUpdateResult result)
        {
            string resultDe = result.Status switch
            {
                WingetUpdateStatus.Successful => "Erfolgreich",
                WingetUpdateStatus.RestartRequired => "Neustart erforderlich",
                WingetUpdateStatus.Cancelled => "Abgebrochen",
                _ => "Fehlgeschlagen"
            };
            string resultEn = result.Status switch
            {
                WingetUpdateStatus.Successful => "Successful",
                WingetUpdateStatus.RestartRequired => "Restart required",
                WingetUpdateStatus.Cancelled => "Cancelled",
                _ => "Failed"
            };

            _settings.ActivityLog.Insert(0, new ActivityLogEntry
            {
                TimestampUtc = DateTime.UtcNow,
                IconGlyph = result.Status is WingetUpdateStatus.Successful or WingetUpdateStatus.RestartRequired
                    ? "\uE895"
                    : "\uEA39",
                TextDe = $"{package.Name}: {resultDe}",
                TextEn = $"{package.Name}: {resultEn}",
                PackageId = package.Id,
                OldVersion = package.Version,
                NewVersion = package.Available,
                Result = result.Status.ToString(),
                ExitCode = result.ExitCode
            });

            while (_settings.ActivityLog.Count > 20)
                _settings.ActivityLog.RemoveAt(_settings.ActivityLog.Count - 1);
            _settings.Save();
        }

        private async Task ShowUpdateSummaryAsync(
            List<(WingetPackage Package, WingetUpdateResult Result)> results,
            int notStartedCount)
        {
            bool en = Localization.CurrentLanguage == "en";
            var panel = new StackPanel { Spacing = 10, MaxWidth = 560 };

            foreach (var item in results)
            {
                string symbol = item.Result.Status switch
                {
                    WingetUpdateStatus.Successful => "✓",
                    WingetUpdateStatus.RestartRequired => "↻",
                    WingetUpdateStatus.Cancelled => "■",
                    _ => "!"
                };
                Windows.UI.Color statusColor = item.Result.Status switch
                {
                    WingetUpdateStatus.Successful => Windows.UI.Color.FromArgb(0xFF, 0x4C, 0xD9, 0x73),
                    WingetUpdateStatus.RestartRequired => Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xC1, 0x4D),
                    WingetUpdateStatus.Cancelled => Windows.UI.Color.FromArgb(0xFF, 0xB0, 0xB0, 0xB0),
                    _ => Windows.UI.Color.FromArgb(0xFF, 0xFF, 0x6B, 0x6B)
                };
                panel.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(10),
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    BorderBrush = new SolidColorBrush(statusColor),
                    Background = (SolidColorBrush)RootGrid.Resources["AppOverlay18"],
                    Padding = new Thickness(12, 10, 12, 10),
                    Child = new TextBlock
                    {
                        Text = $"{symbol}  {item.Package.Name}  ·  {item.Package.Version} → {item.Package.Available}\n{item.Result.Message}",
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = (SolidColorBrush)RootGrid.Resources["AppForegroundBrush"]
                    }
                });
            }

            if (notStartedCount > 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = en ? $"■  {notStartedCount} update(s) were not started." : $"■  {notStartedCount} Update(s) wurden nicht gestartet.",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = (SolidColorBrush)RootGrid.Resources["AppFaintForegroundBrush"]
                });
            }

            var dialog = new ContentDialog
            {
                XamlRoot = RootGrid.XamlRoot,
                Title = en ? "Update summary" : "Update-Abschlussbericht",
                Content = new ScrollViewer { Content = panel, MaxHeight = 430 },
                CloseButtonText = en ? "Close" : "Schließen",
                DefaultButton = ContentDialogButton.Close
            };
            await dialog.ShowAsync();
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

            StorageSelectAllButton.Content = newState ? Localization.T("Common.DeselectAll") : Localization.T("Common.SelectAll");
        }

        // Wandelt den gespeicherten Zeitpunkt der letzten Bereinigung in eine
        // freundliche, relative Anzeige um (z.B. "vor 3 Tagen", "gerade eben").
        private static string FormatLastCleanup(DateTime? lastCleanupUtc)
        {
            bool en = Localization.CurrentLanguage == "en";

            if (lastCleanupUtc == null) return en ? "never" : "noch nie";

            var diff = DateTime.UtcNow - lastCleanupUtc.Value;

            if (diff.TotalMinutes < 1) return en ? "just now" : "gerade eben";
            if (diff.TotalMinutes < 60) return en ? $"{(int)diff.TotalMinutes} minute(s) ago" : $"vor {(int)diff.TotalMinutes} Minute(n)";
            if (diff.TotalHours < 24) return en ? $"{(int)diff.TotalHours} hour(s) ago" : $"vor {(int)diff.TotalHours} Stunde(n)";
            if (diff.TotalDays < 30) return en ? $"{(int)diff.TotalDays} day(s) ago" : $"vor {(int)diff.TotalDays} Tag(en)";

            return lastCleanupUtc.Value.ToLocalTime().ToString("dd.MM.yyyy");
        }

        private async Task LoadStorage()
        {
            if (_isLoadingStorage || _isDeletingStorage) return;
            _isLoadingStorage = true;
            SetGlobalStatus(Localization.CurrentLanguage == "en" ? "Analyzing storage..." : "Speicher wird analysiert...");
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
                _isLoadingStorage = false;
                UpdatesLoadingRing.IsActive = false;
                UpdatesLoadingRing.Visibility = Visibility.Collapsed;
                StorageRefreshButton.IsEnabled = true;
                StorageDeleteSelectedButton.IsEnabled = true;
                SetGlobalStatus(null);
            }

            long totalBytes = categories.Sum(c => c.SizeBytes);
            PageSubtitle.Text = Localization.CurrentLanguage == "en"
                ? $"Total {StorageService.FormatBytes(totalBytes)} reclaimable through cleanup" +
                  $"  •  Last cleaned: {FormatLastCleanup(_settings.LastCleanupUtc)}"
                : $"Insgesamt {StorageService.FormatBytes(totalBytes)} durch Bereinigung freigebbar" +
                  $"  •  Zuletzt bereinigt: {FormatLastCleanup(_settings.LastCleanupUtc)}";
            StorageSelectAllButton.Content = Localization.T("Common.SelectAll");

            var byKey = categories.ToDictionary(c => c.Key);

            // Gruppiert die Kategorien thematisch, damit nicht 20 einzelne
            // Karten untereinander stehen, sondern ausklappbare Abschnitte
            // (gleiches Prinzip wie bei den Systeminfo-Kategorien).
            var groups = new (string Title, string[] Keys)[]
            {
                (Localization.T("Storage.TempFiles"), new[] { "user_temp", "windows_temp", "prefetch", "inet_cache" }),
                (Localization.T("Storage.RecycleDownloads"), new[] { "recycle_bin", "update_cache", "delivery_optimization", "upgrade_logs", "old_install_files" }),
                (Localization.T("Storage.SystemCaches"), new[] { "dx_shader_cache", "thumbnail_cache", "store_cache", "dns_cache" }),
                (Localization.T("Storage.ErrorLogs"), new[] { "wer", "minidump", "crash_dumps", "logs", "setup_logs", "defender_temp" }),
                (Localization.T("Storage.Browser"), new[] { "browser_cache" }),
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
            bool advanced = category.RequiresAdmin || category.Key is "prefetch" or "old_install_files" or "minidump" or "crash_dumps";
            actionsPanel.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(8, 4, 8, 4),
                Background = new SolidColorBrush(advanced
                    ? Windows.UI.Color.FromArgb(0x24, 0xFF, 0xC1, 0x4D)
                    : Windows.UI.Color.FromArgb(0x24, 0x4C, 0xD9, 0x73)),
                Child = new TextBlock
                {
                    Text = advanced
                        ? (Localization.CurrentLanguage == "en" ? "Advanced" : "Erweitert")
                        : (Localization.CurrentLanguage == "en" ? "Safe" : "Sicher"),
                    FontSize = 11,
                    Foreground = new SolidColorBrush(advanced
                        ? Windows.UI.Color.FromArgb(0xFF, 0xA8, 0x68, 0x00)
                        : Windows.UI.Color.FromArgb(0xFF, 0x18, 0x78, 0x3C))
                }
            });
            actionsPanel.Children.Add(toggle);
            actionsPanel.Children.Add(deleteButton);

            var descriptionSuffix = category.RequiresAdmin ? "  •  benötigt evtl. Admin-Rechte" : "";

            var card = new ToolkitControls.SettingsCard
            {
                Header = category.Name,
                Description = $"{category.Description}{descriptionSuffix}  •  {category.SizeDisplay}",
                HeaderIcon = new FontIcon { Glyph = GetStorageIconGlyph(category.Key) },
                Content = actionsPanel,
                BorderThickness = new Thickness(1),
                BorderBrush = (SolidColorBrush)RootGrid.Resources["AppOverlay28"]
            };

            // Akzentfarbener Rand, solange die Kategorie zum Löschen ausgewählt ist.
            var defaultBorder = card.BorderBrush;
            var accentBorder = (SolidColorBrush)RootGrid.Resources["AppAccentBrushLight"];
            toggle.Toggled += (_, __) => card.BorderBrush = toggle.IsOn ? accentBorder : defaultBorder;

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

        // Löscht eine oder mehrere Storage-Kategorien. Kategorien, die
        // Admin-Rechte brauchen, laufen über einen kurzen elevierten
        // Hilfsprozess (ein UAC-Prompt für alle zusammen); alles andere läuft
        // direkt im normalen, nicht elevierten Prozess.
        private async Task<(bool success, string message)> DeleteCategoriesAsync(List<StorageCategory> categories)
        {
            var adminCategories = categories.Where(c => c.RequiresAdmin).ToList();
            var normalCategories = categories.Where(c => !c.RequiresAdmin).ToList();

            var messages = new List<string>();
            bool overallSuccess = true;

            foreach (var category in normalCategories)
            {
                var (success, message) = await StorageService.DeleteCategoryAsync(category);
                messages.Add($"{category.Name}: {message}");
                if (!success) overallSuccess = false;
            }

            if (adminCategories.Count > 0)
            {
                var exitCode = await RunElevatedStorageDeleteAsync(adminCategories);

                if (exitCode == 0)
                {
                    messages.Add(adminCategories.Count == 1
                        ? $"{adminCategories[0].Name}: erfolgreich gelöscht (mit Admin-Rechten)."
                        : $"{adminCategories.Count} Admin-Bereiche erfolgreich gelöscht.");
                }
                else if (exitCode == 1223) // ERROR_CANCELLED - Nutzer hat UAC abgelehnt
                {
                    overallSuccess = false;
                    messages.Add("Admin-Rechte wurden nicht erteilt - Admin-pflichtige Bereiche wurden übersprungen.");
                }
                else
                {
                    overallSuccess = false;
                    messages.Add("Einige Admin-pflichtige Bereiche konnten nicht (vollständig) gelöscht werden.");
                }
            }

            return (overallSuccess, string.Join("  •  ", messages));
        }

        // Startet den elevierten Hilfsprozess für Admin-pflichtige Löschungen
        // und liefert dessen Exitcode zurück (0 = alles erfolgreich).
        private async Task<int> RunElevatedStorageDeleteAsync(List<StorageCategory> categories)
        {
            try
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath))
                    throw new InvalidOperationException("Eigener Programmpfad konnte nicht ermittelt werden.");

                var keyList = string.Join(";", categories.Select(c => c.Key));

                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = $"--delete-storage \"{keyList}\"",
                    UseShellExecute = true,
                    Verb = "runas" // löst den UAC-Prompt nur für diesen einen Vorgang aus
                };

                using var proc = Process.Start(psi);
                if (proc != null) await proc.WaitForExitAsync();

                return proc?.ExitCode ?? -1;
            }
            catch (System.ComponentModel.Win32Exception winEx) when (winEx.NativeErrorCode == 1223)
            {
                // Nutzer hat den UAC-Prompt mit "Nein" abgebrochen
                Logger.Log("Elevierte Storage-Löschung vom Nutzer abgebrochen (UAC verweigert).");
                return 1223;
            }
            catch (Exception ex)
            {
                Logger.LogError("RunElevatedStorageDeleteAsync", ex);
                return -1;
            }
        }

        private async Task DeleteSingleCategory(StorageCategory category, Button sourceButton)
        {
            if (_isDeletingStorage || _isLoadingStorage) return;

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
            StorageProgressText.Text = category.RequiresAdmin
                ? $"Lösche {category.Name}... (Admin-Bestätigung nötig)"
                : $"Lösche {category.Name}...";

            var (success, message) = await DeleteCategoriesAsync(new List<StorageCategory> { category });
            Logger.Log($"Storage-Löschung '{category.Name}': {(success ? "OK" : "Fehler")} - {message}");

            if (success)
            {
                _settings.LastCleanupUtc = DateTime.UtcNow;
                _settings.Save();
                LogActivity("\uE74D",
                    $"{category.Name} bereinigt ({category.SizeDisplay})",
                    $"Cleaned {category.Name} ({category.SizeDisplay})");
            }

            StorageProgressBar.Value = 1;
            StorageProgressText.Text = success
                ? $"{category.Name}: {message}"
                : $"{category.Name} - Fehler: {message}";

            await Task.Delay(1500);
            StorageProgressPanel.Visibility = Visibility.Collapsed;

            _isDeletingStorage = false;
            await LoadStorage();
        }

        private async void StorageDeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            if (_isDeletingStorage || _isLoadingStorage) return;
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
            _isDeletingStorage = true;
            _isDeletingStorage = true;

            StorageRefreshButton.IsEnabled = false;
            StorageDeleteSelectedButton.IsEnabled = false;

            StorageProgressPanel.Visibility = Visibility.Visible;

            var normalCategories = selected.Where(c => !c.RequiresAdmin).ToList();
            var adminCategories = selected.Where(c => c.RequiresAdmin).ToList();

            StorageProgressBar.Maximum = normalCategories.Count + (adminCategories.Count > 0 ? 1 : 0);
            StorageProgressBar.Value = 0;

            var results = new List<string>();
            bool anySuccess = false;
            int step = 0;

            foreach (var category in normalCategories)
            {
                step++;
                StorageProgressText.Text = $"Lösche {category.Name} ({step}/{StorageProgressBar.Maximum})...";

                var (success, message) = await StorageService.DeleteCategoryAsync(category);
                results.Add(success ? $"{category.Name}: OK" : $"{category.Name}: Fehler");
                Logger.Log($"Storage-Sammel-Löschung '{category.Name}': {(success ? "OK" : "Fehler")} - {message}");
                if (success) anySuccess = true;

                StorageProgressBar.Value = step;
            }

            if (adminCategories.Count > 0)
            {
                step++;
                StorageProgressText.Text = $"Lösche {adminCategories.Count} Admin-Bereich(e)... (Admin-Bestätigung nötig)";

                var exitCode = await RunElevatedStorageDeleteAsync(adminCategories);
                bool adminSuccess = exitCode == 0;

                foreach (var category in adminCategories)
                {
                    results.Add(adminSuccess ? $"{category.Name}: OK" : $"{category.Name}: Fehler");
                    Logger.Log($"Storage-Sammel-Löschung (elevated) '{category.Name}': {(adminSuccess ? "OK" : $"Fehler (ExitCode {exitCode})")}");
                }

                if (adminSuccess) anySuccess = true;
                StorageProgressBar.Value = step;
            }

            StorageProgressText.Text = "Bereinigung abgeschlossen: " + string.Join(", ", results);

            if (anySuccess)
            {
                _settings.LastCleanupUtc = DateTime.UtcNow;
                _settings.Save();

                long totalFreedBytes = selected.Sum(c => c.SizeBytes);
                var freedDisplay = StorageService.FormatBytes(totalFreedBytes);
                LogActivity("\uE74D",
                    $"{selected.Count} Bereich(e) bereinigt ({freedDisplay})",
                    $"Cleaned {selected.Count} area(s) ({freedDisplay})");
            }

            await Task.Delay(2500);
            StorageProgressPanel.Visibility = Visibility.Collapsed;

            _isDeletingStorage = false;
            await LoadStorage();
        }

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

        private async Task LoadInstalledPrograms()
        {
            if (_isLoadingPrograms) return;
            _isLoadingPrograms = true;
            UninstallPanel.Children.Clear();
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

        private void UninstallSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var query = UninstallSearchBox.Text?.Trim() ?? "";
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

        private async Task UninstallProgramAsync(InstalledProgram program, Button sourceButton)
        {
            bool confirmed = await ConfirmAsync(
                "Programm deinstallieren?",
                $"\"{program.DisplayName}\" wird deinstalliert. Es öffnet sich ggf. ein eigenes Deinstallations-Fenster des Programms. Fortfahren?");

            if (!confirmed) return;

            sourceButton.IsEnabled = false;

            var (success, message) = InstalledProgramsService.Uninstall(program);
            Logger.Log($"Deinstallation '{program.DisplayName}': {(success ? "gestartet" : "Fehler")} - {message}");

            if (success)
            {
                LogActivity("\uE74D",
                    $"{program.DisplayName} deinstalliert",
                    $"{program.DisplayName} uninstalled");
            }

            var dialog = new ContentDialog
            {
                Title = success ? "Deinstallation gestartet" : "Fehler",
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = this.Content.XamlRoot
            };
            await dialog.ShowAsync();

            sourceButton.IsEnabled = true;
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
