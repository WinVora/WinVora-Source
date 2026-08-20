using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ToolkitControls = CommunityToolkit.WinUI.Controls;

namespace WinVora
{
    public sealed partial class MainWindow
    {
        private int _extendedSystemInfoGeneration;
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

            // Die Kategorie steht bereits im Expander. Ein zweiter Header in
            // SettingsCard reserviert sonst fast die halbe Kartenbreite.
            foreach (var card in new[]
            {
                SysCardDevice, SysCardOs, SysCardCpu, SysCardRam, SysCardBoard,
                SysCardSecurity, SysCardGpu, SysCardDrives, SysCardNetwork, SysCardBattery
            })
            {
                card.ClearValue(ToolkitControls.SettingsCard.HeaderProperty);
                card.ClearValue(ToolkitControls.SettingsCard.DescriptionProperty);
                card.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            }

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
            if (!TrySetPage("System")) return;
            await LoadSystemSnapshotIfNeededAsync(
                Localization.T("Common.Loading"),
                Localization.T("System.LoadError"));
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
                SysGpuPanel.Children.Add(MakeSystemInfoRow(en ? "No GPU detected" : "Keine GPU erkannt", ""));
            }
            foreach (var gpu in s.Gpus)
            {
                SysGpuPanel.Children.Add(MakeSystemInfoRow(en ? "Graphics Card" : "Grafikkarte", gpu));
            }

            SysDrivesPanel.Children.Clear();
            foreach (var drive in s.Drives)
            {
                SysDrivesPanel.Children.Add(MakeSystemInfoRow(drive.Name, drive.TotalSize,
                    en ? $"{drive.FreeSpace} free" : $"{drive.FreeSpace} frei"));
            }

            SysNetworkPanel.Children.Clear();
            if (s.NetworkAdapters.Length == 0)
            {
                SysNetworkPanel.Children.Add(MakeSystemInfoRow(en ? "No active network adapter found" : "Kein aktiver Netzwerkadapter gefunden", ""));
            }
            foreach (var net in s.NetworkAdapters)
            {
                SysNetworkPanel.Children.Add(MakeSystemInfoRow(
                    net.Name,
                    $"IPv4: {net.IPv4}  •  MAC: {net.MacAddress}",
                    en ? $"Gateway: {net.Gateway}\nDNS: {net.Dns}" : $"Gateway: {net.Gateway}\nDNS: {net.Dns}"));
            }

            SysBattery.Text = s.BatteryStatus;
            _ = RefreshExtendedSystemInfoDetailsAsync();
            if (_currentPageKey == "System")
                PageSubtitle.Text = Localization.CurrentLanguage == "en"
                    ? $"Last checked: {DateTime.Now:G}"
                    : $"Zuletzt geprüft: {DateTime.Now:G}";
            // Nur ein ausdrücklich deaktivierter Schutz löst eine Warnung aus.
            // WMI liefert Defender/Firewall auf manchen PCs als "Unbekannt"
            // oder "Nicht verfügbar"; das ist kein Beleg für ein
            // Sicherheitsproblem und darf den Dashboardstatus nicht dauerhaft
            // gelb färben.
            if (!string.IsNullOrWhiteSpace(s.DefenderStatus) &&
                !string.IsNullOrWhiteSpace(s.FirewallStatus))
            {
                ApplyDashboardSecurityStatus(s.DefenderStatus, s.FirewallStatus);
            }
        }

        private async Task RefreshExtendedSystemInfoDetailsAsync()
        {
            int generation = ++_extendedSystemInfoGeneration;
            bool initialEnglish = Localization.CurrentLanguage == "en";
            string loading = initialEnglish ? "Loading live sensor data..." : "Live-Sensordaten werden geladen...";
            string cpuBase = SysCpuDetails.Text;
            string boardBase = SysMainboard.Text;
            string batteryBase = SysBattery.Text;
            SysCpuDetails.Text = $"{cpuBase}\n{loading}";
            SysMainboard.Text = $"{boardBase}\n{loading}";
            SysBattery.Text = $"{batteryBase}  •  {loading}";
            var gpuLoading = MakeSystemInfoRow(initialEnglish ? "Live sensors" : "Live-Sensoren", loading);
            var driveLoading = MakeSystemInfoRow(initialEnglish ? "Drive sensors" : "Laufwerkssensoren", loading);
            var networkLoading = MakeSystemInfoRow(initialEnglish ? "Network diagnostics" : "Netzwerkdiagnose", loading);
            SysGpuPanel.Children.Add(gpuLoading);
            SysDrivesPanel.Children.Add(driveLoading);
            SysNetworkPanel.Children.Add(networkLoading);
            try
            {
                var details = await Task.Run(() =>
                {
                    var telemetry = HardwareTelemetryService.GetSnapshot(refreshSensors: true);
                    return (Hardware: telemetry.Sensors,
                        BatteryHealth: ExtendedPcCheckService.ReadBatteryHealthPercent(),
                        NetworkErrors: ExtendedPcCheckService.ReadNetworkErrorCount());
                });
                if (_currentPageKey != "System" || generation != _extendedSystemInfoGeneration) return;
                bool en = Localization.CurrentLanguage == "en";

                SysCpuDetails.Text = cpuBase;
                SysMainboard.Text = boardBase;
                SysBattery.Text = batteryBase;
                SysGpuPanel.Children.Remove(gpuLoading);
                SysDrivesPanel.Children.Remove(driveLoading);
                SysNetworkPanel.Children.Remove(networkLoading);

                var cpuParts = new List<string>();
                if (details.Hardware.CpuClockMhz is double clock) cpuParts.Add($"{(en ? "Live clock" : "Live-Takt")}: {clock:0} MHz");
                if (details.Hardware.CpuTemperature is double cpuTemp) cpuParts.Add($"{(en ? "Temperature" : "Temperatur")}: {cpuTemp:0} °C");
                if (details.Hardware.CpuPowerWatts is double cpuPower && cpuPower > 0.5) cpuParts.Add($"{(en ? "Power" : "Leistung")}: {cpuPower:0.0} W");
                if (cpuParts.Count > 0)
                    SysCpuDetails.Text += $"\n{string.Join("  •  ", cpuParts)}";

                var activeFans = details.Hardware.Fans.Where(f => f.Rpm > 0).ToList();
                if (activeFans.Count > 0)
                    SysMainboard.Text += $"\n{(en ? "Fans" : "Lüfter")}: {string.Join("  •  ", activeFans.Select(f => $"{f.Name}: {f.Rpm:0} RPM"))}";
                else SysMainboard.Text += $"\n{(en ? "Fan sensors: Not available" : "Lüftersensoren: Nicht verfügbar")}";

                if (details.Hardware.GpuLoadPercent is double gpuLoad ||
                    details.Hardware.GpuTemperature is double || details.Hardware.GpuPowerWatts is double)
                {
                    var gpuParts = new List<string>();
                    if (details.Hardware.GpuLoadPercent is double load) gpuParts.Add($"{(en ? "Usage" : "Auslastung")}: {load:0} %");
                    if (details.Hardware.GpuTemperature is double temp) gpuParts.Add($"{(en ? "Temperature" : "Temperatur")}: {temp:0} °C");
                    if (details.Hardware.GpuPowerWatts is double power && power > 0.5) gpuParts.Add($"{(en ? "Power" : "Leistung")}: {power:0.0} W");
                    SysGpuPanel.Children.Add(MakeSystemInfoRow(en ? "Live sensors" : "Live-Sensoren", string.Join("  •  ", gpuParts)));
                }
                else SysGpuPanel.Children.Add(MakeSystemInfoRow(en ? "Live sensors" : "Live-Sensoren", en ? "Not available" : "Nicht verfügbar"));

                foreach (var storage in details.Hardware.Storage)
                {
                    var parts = new List<string>();
                    if (storage.Temperature is double temp) parts.Add($"{(en ? "Temperature" : "Temperatur")}: {temp:0} °C");
                    if (storage.RemainingLife is double life) parts.Add($"{(en ? "Remaining life" : "Verbleibende Lebensdauer")}: {life:0} %");
                    if (parts.Count > 0) SysDrivesPanel.Children.Add(MakeSystemInfoRow(storage.Name, string.Join("  •  ", parts)));
                }
                if (details.Hardware.Storage.Count == 0)
                    SysDrivesPanel.Children.Add(MakeSystemInfoRow(en ? "SMART sensors" : "SMART-Sensoren", en ? "Not available" : "Nicht verfügbar"));

                if (details.NetworkErrors is long errors)
                    SysNetworkPanel.Children.Add(MakeSystemInfoRow(en ? "Packet errors since startup" : "Paketfehler seit dem Start", errors.ToString("N0")));
                else SysNetworkPanel.Children.Add(MakeSystemInfoRow(en ? "Packet errors" : "Paketfehler", en ? "Not available" : "Nicht verfügbar"));

                if (details.BatteryHealth is double health)
                    SysBattery.Text += $"  •  {(en ? "Health" : "Gesundheit")}: {health:0} %";
                else if (!batteryBase.Contains(en ? "No battery" : "Kein Akku", StringComparison.OrdinalIgnoreCase))
                    SysBattery.Text += $"  •  {(en ? "Health: Not available" : "Gesundheit: Nicht verfügbar")}";
            }
            catch (Exception ex)
            {
                Logger.LogErrorOnce("Erweiterte Systeminfos darstellen", ex);
                if (_currentPageKey == "System" && generation == _extendedSystemInfoGeneration)
                {
                    bool en = Localization.CurrentLanguage == "en";
                    SysCpuDetails.Text = cpuBase;
                    SysMainboard.Text = $"{boardBase}\n{(en ? "Fan sensors: Not available" : "Lüftersensoren: Nicht verfügbar")}";
                    SysBattery.Text = batteryBase;
                    SysGpuPanel.Children.Remove(gpuLoading);
                    SysDrivesPanel.Children.Remove(driveLoading);
                    SysNetworkPanel.Children.Remove(networkLoading);
                    SysGpuPanel.Children.Add(MakeSystemInfoRow(en ? "Live sensors" : "Live-Sensoren", en ? "Not available" : "Nicht verfügbar"));
                    SysDrivesPanel.Children.Add(MakeSystemInfoRow(en ? "SMART sensors" : "SMART-Sensoren", en ? "Not available" : "Nicht verfügbar"));
                    SysNetworkPanel.Children.Add(MakeSystemInfoRow(en ? "Network diagnostics" : "Netzwerkdiagnose", en ? "Not available" : "Nicht verfügbar"));
                }
            }
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

        // Gemeinsame Karte für Verlauf und weitere dynamische Inhalte.
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

            var originalBorder = item.BorderBrush;
            item.PointerEntered += (_, __) =>
            {
                item.Background = (SolidColorBrush)RootGrid.Resources["AppOverlay28"];
                if (statusBorder == null)
                    item.BorderBrush = (SolidColorBrush)RootGrid.Resources["AppAccentBrushLight"];
            };
            item.PointerExited += (_, __) =>
            {
                item.Background = (SolidColorBrush)RootGrid.Resources["AppOverlay18"];
                item.BorderBrush = originalBorder;
            };

            item.Shadow = new ThemeShadow();
            item.Translation = new System.Numerics.Vector3(0, 0, 12);

            var panel = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Stretch };
            panel.Children.Add(new TextBlock
            {
                Text = header,
                FontSize = 17,
                Foreground = (SolidColorBrush)RootGrid.Resources["AppForegroundBrush"],
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });

            if (!string.IsNullOrWhiteSpace(description))
                panel.Children.Add(new TextBlock
                {
                    Text = description,
                    FontSize = 15,
                    Foreground = (SolidColorBrush)RootGrid.Resources["AppForegroundC0"],
                    TextWrapping = TextWrapping.Wrap
                });

            if (!string.IsNullOrWhiteSpace(content))
                panel.Children.Add(new TextBlock
                {
                    Text = content,
                    FontSize = 15,
                    Foreground = (SolidColorBrush)RootGrid.Resources["AppForegroundD8"],
                    TextWrapping = TextWrapping.Wrap
                });

            item.Child = panel;
            return item;
        }

        // Schlichte Informationszeilen innerhalb der großen Systeminfo-Karte.
        // So entstehen bei GPU, Laufwerken und Netzwerk keine Karten-in-Karten.
        private Border MakeSystemInfoRow(
            string header,
            string description,
            string? content = null)
        {
            var item = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MinHeight = 64,
                Padding = new Thickness(0, 12, 0, 12),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderBrush = (SolidColorBrush)RootGrid.Resources["AppOverlay22"],
                BorderThickness = new Thickness(0, 0, 0, 1)
            };

            var panel = new Grid
            {
                ColumnSpacing = 32,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var headerText = new TextBlock
            {
                Text = header,
                FontSize = 16,
                Foreground = (SolidColorBrush)RootGrid.Resources["AppForegroundBrush"],
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(headerText, 0);
            panel.Children.Add(headerText);

            var values = new StackPanel
            {
                Spacing = 5,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(values, 1);

            if (!string.IsNullOrWhiteSpace(description))
            {
                values.Children.Add(new TextBlock
                {
                    Text = description,
                    FontSize = 15,
                    Foreground = (SolidColorBrush)RootGrid.Resources["AppForegroundC0"],
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Right
                });
            }

            if (!string.IsNullOrWhiteSpace(content))
            {
                values.Children.Add(new TextBlock
                {
                    Text = content,
                    FontSize = 15,
                    Foreground = (SolidColorBrush)RootGrid.Resources["AppForegroundD8"],
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Right
                });
            }

            panel.Children.Add(values);

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
        private int _liveUsageTickActive;
        private readonly Queue<double> _cpuHistory = new();
        private readonly Queue<double> _ramHistory = new();
        private readonly Queue<double> _gpuHistory = new();
        private const int HistoryMaxPoints = 30;

        private void StartLiveUsageTimer()
        {
            _liveUsageTimer?.Stop();
            _hardwareTickCounter = 0;
            _liveUsageTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(_settings.LiveUpdateIntervalSeconds) };
            _liveUsageTimer.Tick += LiveUsageTimer_Tick;

            _liveUsageTimer.Start();

            // BUGFIX: Vorher stand überall "--%", bis das erste Timer-Intervall
            // verstrichen war (Standard 2 Sekunden, bei größerem Intervall
            // entsprechend länger) - jetzt wird sofort einmal aktualisiert,
            // statt auf den ersten Tick zu warten. Die GPU-Drosselung (nur
            // jeder 3. Tick) wird für diesen allerersten Aufruf bewusst
            // übersprungen, sonst würde GPU trotzdem erst nach 2-3 Intervallen
            // erscheinen.
            _ = RunLiveUsageTickAsync(forceHardwareRead: true);
        }

        private async void LiveUsageTimer_Tick(object? sender, object e)
        {
            try { await RunLiveUsageTickAsync(forceHardwareRead: false); }
            catch (OperationCanceledException) when (_startupCancellation.IsCancellationRequested) { }
            catch (Exception ex) { Logger.LogErrorOnce("Live-Hardwarewerte aktualisieren", ex); }
        }

        private async Task RunLiveUsageTickAsync(bool forceHardwareRead)
        {
            if (Interlocked.Exchange(ref _liveUsageTickActive, 1) != 0) return;
            try { await UpdateLiveUsageAsync(forceHardwareRead); }
            finally { Volatile.Write(ref _liveUsageTickActive, 0); }
        }

        private async Task UpdateLiveUsageAsync(bool forceHardwareRead = false)
        {
            // Läuft im Hintergrund, damit der UI-Thread (und damit das
            // Scrollen) nicht alle 2 Sekunden kurz blockiert wird.
            _hardwareTickCounter++;
            bool refreshSensors = forceHardwareRead || _hardwareTickCounter % 3 == 0;
            var telemetry = await HardwareTelemetryService.GetSnapshotAsync(
                refreshSensors, _startupCancellation.Token);
            double cpu = telemetry.CpuPercent;
            double ram = telemetry.RamPercent;
            double ramUsedGb = telemetry.RamUsedGb;
            double ramTotalGb = telemetry.RamTotalGb;

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
            if (refreshSensors)
            {
                var readings = telemetry.Sensors;

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
