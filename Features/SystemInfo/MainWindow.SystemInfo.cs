using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace WinVora
{
    public sealed partial class MainWindow
    {
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
            if (_currentPageKey == "System")
                PageSubtitle.Text = Localization.CurrentLanguage == "en"
                    ? $"Last checked: {DateTime.Now:G}"
                    : $"Zuletzt geprüft: {DateTime.Now:G}";
            // Nur ein ausdrücklich deaktivierter Schutz löst eine Warnung aus.
            // WMI liefert Defender/Firewall auf manchen PCs als "Unbekannt"
            // oder "Nicht verfügbar"; das ist kein Beleg für ein
            // Sicherheitsproblem und darf den Dashboardstatus nicht dauerhaft
            // gelb färben.
            ApplyDashboardSecurityStatus(s.DefenderStatus, s.FirewallStatus);
        }

        private void ApplyDashboardSecurityStatus(string antivirusStatus, string firewallStatus)
        {
            bool en = Localization.CurrentLanguage == "en";
            _lastAntivirusStatus = antivirusStatus;
            _lastFirewallStatus = firewallStatus;
            _securityHealthState = SecurityStatusEvaluator.Evaluate(antivirusStatus, firewallStatus);
            HealthSecurityText.Text = _securityHealthState switch
            {
                SecurityHealthState.Active => en ? "Active" : "Aktiv",
                SecurityHealthState.Problem => en ? "Check" : "Prüfen",
                _ => en ? "Not verifiable" : "Nicht prüfbar"
            };
            HealthSecurityText.Foreground = new SolidColorBrush(_securityHealthState switch
            {
                SecurityHealthState.Active => GetHealthyStatusColor(),
                SecurityHealthState.Problem => GetStatusColor("AppWarningBrush"),
                _ => GetStatusColor("AppNeutralStatusBrush")
            });
            UpdateDashboardStatusSummary();
        }

        // Das helle Grün wirkt auf dunklem Untergrund ruhig, auf Weiß jedoch
        // deutlich zu leuchtend. Im Hellmodus verwenden wir deshalb dieselbe
        // Bedeutung mit einem dunkleren, kontrastreichen Grünton.
        private Windows.UI.Color GetHealthyStatusColor() => GetStatusColor("AppSuccessBrush");

        private Windows.UI.Color GetStatusColor(string resourceKey) =>
            RootGrid.Resources[resourceKey] is SolidColorBrush brush
                ? brush.Color
                : Microsoft.UI.Colors.Gray;

        private async void SecurityCard_Tapped(object sender, TappedRoutedEventArgs e)
        {
            bool en = Localization.CurrentLanguage == "en";
            string explanation = _securityHealthState switch
            {
                SecurityHealthState.Active => en ? "No security problem was detected." : "Es wurde kein Sicherheitsproblem erkannt.",
                SecurityHealthState.Problem => en ? "At least one protection component reports a problem." : "Mindestens eine Schutzkomponente meldet ein Problem.",
                _ => en ? "At least one value could not be checked reliably without additional permissions." : "Mindestens ein Wert konnte ohne zusätzliche Rechte nicht zuverlässig geprüft werden."
            };
            await new ContentDialog
            {
                XamlRoot = RootGrid.XamlRoot,
                Title = en ? "Security details" : "Sicherheitsdetails",
                Content = new TextBlock
                {
                    Text = $"{explanation}\n\n{(en ? "Antivirus" : "Virenschutz")}: {_lastAntivirusStatus}\n{(en ? "Firewall" : "Firewall")}: {_lastFirewallStatus}",
                    TextWrapping = TextWrapping.Wrap
                },
                CloseButtonText = en ? "Close" : "Schließen"
            }.ShowAsync();
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

            var headerText = new TextBlock
            {
                Text = header,
                FontSize = 17,
                Foreground = (SolidColorBrush)RootGrid.Resources["AppForegroundBrush"],
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            };
            panel.Children.Add(headerText);

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
            => EmptyStateUiBuilder.Create(glyph, title, description, RootGrid.Resources, actionText, action);



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
                    tempText = Environment.OSVersion.Version.Build > 0
                        ? $"Windows (Build {Environment.OSVersion.Version.Build})"
                        : "Windows";

                DashTempText.Text = tempText;
                LblDashTemp.Text = readings.CpuTemperature != null || readings.GpuTemperature != null
                    ? (Localization.CurrentLanguage == "en" ? "Temperature" : "Temperatur")
                    : (Localization.CurrentLanguage == "en" ? "Windows version" : "Windows-Version");
                DashTempText.Foreground = (SolidColorBrush)RootGrid.Resources["AppForegroundBrush"];
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

            // Die Programmliste wird erst nach dem sichtbaren Hauptfenster
            // speicherschonend und ohne Icons im Hintergrund geladen.
            DashInstalledCountText.Text = _installedPrograms.Count > 0
                ? _installedPrograms.Count.ToString()
                : "…";

            UpdateDashboardStatusSummary();

            // Ordnergrößen können auf großen Profilen länger dauern. Diese
            // neue Komfortfunktion darf deshalb niemals den Ladebildschirm
            // oder das Öffnen des Hauptfensters verzögern.
        }


        private void ScheduleDashboardRefresh()
        {
            _dashboardRefreshDebounce?.Cancel();
            _dashboardRefreshDebounce?.Dispose();
            var cancellation = _dashboardRefreshDebounce = new CancellationTokenSource();
            _ = RefreshDashboardAfterDelayAsync(cancellation);
        }

        private async Task RefreshDashboardAfterDelayAsync(CancellationTokenSource owner)
        {
            try
            {
                await Task.Delay(300, owner.Token);
                await PopulateDashboardWidgetsAsync();
            }
            catch (OperationCanceledException)
            {
                // Eine neuere Änderung übernimmt die Aktualisierung.
            }
            catch (Exception ex)
            {
                Logger.LogError("Dashboard-Aktualisierung", ex);
            }
            finally
            {
                if (ReferenceEquals(_dashboardRefreshDebounce, owner))
                {
                    _dashboardRefreshDebounce = null;
                    owner.Dispose();
                }
            }
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

            bool securityOk = _securityHealthState == SecurityHealthState.Active;
            bool securityUnknown = _securityHealthState == SecurityHealthState.Unknown;

            var green = new SolidColorBrush(GetHealthyStatusColor());
            var yellow = (SolidColorBrush)RootGrid.Resources["AppWarningBrush"];
            DashUpdatesStatusDot.Fill = updateCount == 0 ? green : yellow;
            var gray = (SolidColorBrush)RootGrid.Resources["AppNeutralStatusBrush"];
            DashSecurityStatusDot.Fill = securityOk ? green : securityUnknown ? gray : yellow;
            DashUpdatesStatusText.Text = updateCount == 0
                ? (en ? "No updates" : "Keine Updates")
                : (en ? $"{updateCount} update(s)" : $"{updateCount} Update(s)");
            DashSecurityStatusText.Text = securityOk
                ? (en ? "Security active" : "Sicherheit aktiv")
                : securityUnknown
                    ? (en ? "Security not verifiable" : "Sicherheit nicht prüfbar")
                    : (en ? "Check security" : "Sicherheit prüfen");
            DashSystemStatusText.Text = en ? "System monitoring running" : "Systemüberwachung läuft";
            bool everythingOk = updateCount == 0 && securityOk;
            DashOverallBadgeText.Text = everythingOk
                ? (en ? "Everything looks good" : "Alles in Ordnung")
                : updateCount == 0 && securityUnknown
                    ? (en ? "Security not verifiable" : "Sicherheit nicht prüfbar")
                : updateCount > 0 && !securityOk
                    ? (en ? "Updates and security need attention" : "Updates und Sicherheit prüfen")
                    : updateCount > 0
                        ? (en ? "Updates available" : "Updates verfügbar")
                        : (en ? "Check security" : "Sicherheit prüfen");
            DashOverallBadgeText.Foreground = everythingOk
                ? green
                : updateCount == 0 && securityUnknown ? gray : yellow;

            if (updateCount == 0 && securityOk)
            {
                DashOverallStatusText.Text = en
                    ? "No updates · Security active · System monitoring running"
                    : "Keine Updates · Sicherheit aktiv · Systemüberwachung läuft";
            }
            else if (updateCount > 0)
            {
                DashOverallStatusText.Text = en
                    ? $"{updateCount} update(s) available · {(securityOk ? "Security active" : "Check security")}"
                    : $"{updateCount} Update(s) verfügbar · {(securityOk ? "Sicherheit aktiv" : "Sicherheit prüfen")}";
            }
            else
            {
                DashOverallStatusText.Text = Localization.T("Dash.PleaseCheck");
            }
        }

        private void DashUpdates_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
            => Updates_Click(sender, new RoutedEventArgs());

        private void DashDisk_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
            => Cleaner_Click(sender, new RoutedEventArgs());

        private void DashPrograms_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
            => Uninstaller_Click(sender, new RoutedEventArgs());
    }
}
