using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WinVora
{
    public sealed partial class MainWindow
    {
        private async Task ShowInternalDiagnosticsAsync()
        {
            bool en = Localization.CurrentLanguage == "en";
            CancellationToken token = _startupCancellation.Token;
            var checks = await Task.WhenAll(
                CheckWingetAsync(en, token),
                CheckWmiAsync(en, token),
                Task.FromResult(CheckDefender(en)),
                Task.FromResult(CheckNotifications(en)));
            var panel = new StackPanel { Spacing = 8, Width = 480 };
            foreach (var check in checks)
            {
                var row = new Grid { ColumnSpacing = 12 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new TextBlock { Text = check.Name, VerticalAlignment = VerticalAlignment.Center });
                var status = new TextBlock
                {
                    Text = check.Message,
                    Foreground = (SolidColorBrush)RootGrid.Resources[check.Ok ? "AppSuccessBrush" : "AppWarningBrush"]
                };
                Grid.SetColumn(status, 1); row.Children.Add(status);
                panel.Children.Add(new Border
                {
                    Padding = new Thickness(12, 9, 12, 9),
                    CornerRadius = new CornerRadius(8),
                    Background = (SolidColorBrush)RootGrid.Resources["AppOverlay18"],
                    Child = row
                });
                if (!check.Ok)
                {
                    panel.Children.Add(new TextBlock
                    {
                        Text = en
                            ? "Recommendation: restart WinVora and Windows first. If the problem remains, include a support report."
                            : "Empfehlung: Starte zunächst WinVora und Windows neu. Bleibt das Problem bestehen, füge einen Supportbericht bei.",
                        TextWrapping = TextWrapping.Wrap,
                        FontSize = 11,
                        Opacity = 0.75
                    });
                }
            }
            string technicalDetails = string.Join("\n", checks.Select(check =>
                check.Name + ": " + check.Message + " (" + (check.Ok ? "OK" : "Problem") + ")"));
            var copyDetails = new Button
            {
                Content = en ? "Copy technical details" : "Technische Details kopieren",
                HorizontalAlignment = HorizontalAlignment.Left
            };
            copyDetails.Click += (_, __) =>
            {
                var data = new Windows.ApplicationModel.DataTransfer.DataPackage();
                data.SetText(technicalDetails);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
                copyDetails.Content = en ? "Copied" : "Kopiert";
            };
            panel.Children.Add(copyDetails);
            var dialog = CommonUiBuilder.CreateConfirmation(
                RootGrid.XamlRoot,
                en ? "WinVora status" : "WinVora-Status",
                panel,
                null,
                en ? "Close" : "Schließen");
            await dialog.ShowAsync();
        }

        private static async Task<(string Name, bool Ok, string Message)> CheckWingetAsync(
            bool en,
            CancellationToken cancellationToken)
        {
            try
            {
                ProcessRunResult result = await SystemAccess.ProcessRunner.RunAsync(
                    new ProcessStartInfo("winget", "--version"),
                    TimeSpan.FromSeconds(5),
                    cancellationToken).ConfigureAwait(false);
                string version = result.StandardOutput.Trim();
                bool ok = !result.TimedOut && result.ExitCode == 0 && !string.IsNullOrWhiteSpace(version);
                return ("WinGet", ok, result.TimedOut
                    ? (en ? "Timed out" : "Zeitüberschreitung")
                    : string.IsNullOrWhiteSpace(version) ? (en ? "Not available" : "Nicht verfügbar") : version);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Logger.LogErrorOnce("Interne Diagnose: WinGet", ex);
                return ("WinGet", false, en ? "Not available" : "Nicht verfügbar");
            }
        }

        private static async Task<(string Name, bool Ok, string Message)> CheckWmiAsync(
            bool en,
            CancellationToken cancellationToken)
        {
            try
            {
                var result = await Task.Run(
                    () => SystemAccess.Wmi.Query(
                        @"root\CIMV2",
                        "SELECT Caption FROM Win32_OperatingSystem",
                        "Caption"),
                    cancellationToken).WaitAsync(TimeSpan.FromSeconds(6), cancellationToken).ConfigureAwait(false);
                return ("WMI", result.Count > 0, result.Count > 0 ? "OK" : (en ? "No response" : "Keine Antwort"));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Logger.LogErrorOnce("Interne Diagnose: WMI", ex);
                return ("WMI", false, en ? "Not available" : "Nicht verfügbar");
            }
        }

        private (string Name, bool Ok, string Message) CheckDefender(bool en)
        {
            string value = _cachedSnapshot?.DefenderStatus ?? "";
            bool ok = value.Contains("Aktiv", StringComparison.OrdinalIgnoreCase) || value.Contains("Active", StringComparison.OrdinalIgnoreCase);
            return ("Defender", ok, string.IsNullOrWhiteSpace(value) ? (en ? "Not checked" : "Nicht geprüft") : value);
        }

        private static (string Name, bool Ok, string Message) CheckNotifications(bool en)
        {
            try
            {
                bool supported = Microsoft.Windows.AppNotifications.AppNotificationManager.IsSupported();
                return (en ? "Notifications" : "Benachrichtigungen", supported,
                    supported ? "OK" : (en ? "Not supported" : "Nicht unterstützt"));
            }
            catch { return (en ? "Notifications" : "Benachrichtigungen", false, en ? "Not available" : "Nicht verfügbar"); }
        }
    }
}
