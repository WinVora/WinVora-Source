using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Diagnostics;
using System.Management;
using System.Linq;
using System.Threading.Tasks;

namespace WinVora
{
    public sealed partial class MainWindow
    {
        private async Task ShowInternalDiagnosticsAsync()
        {
            bool en = Localization.CurrentLanguage == "en";
            var checks = await Task.Run(() => new[]
            {
                CheckWinget(en),
                CheckWmi(en),
                CheckDefender(en),
                CheckNotifications(en)
            });
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

        private static (string Name, bool Ok, string Message) CheckWinget(bool en)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo("winget", "--version")
                {
                    RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true
                });
                string version = process?.StandardOutput.ReadToEnd().Trim() ?? "";
                process?.WaitForExit(3000);
                return ("WinGet", process?.ExitCode == 0, string.IsNullOrWhiteSpace(version) ? (en ? "Not available" : "Nicht verfügbar") : version);
            }
            catch { return ("WinGet", false, en ? "Not available" : "Nicht verfügbar"); }
        }

        private static (string Name, bool Ok, string Message) CheckWmi(bool en)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Caption FROM Win32_OperatingSystem");
                using var result = searcher.Get();
                return ("WMI", result.Count > 0, result.Count > 0 ? "OK" : (en ? "No response" : "Keine Antwort"));
            }
            catch { return ("WMI", false, en ? "Not available" : "Nicht verfügbar"); }
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
