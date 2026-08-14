using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using ToolkitControls = CommunityToolkit.WinUI.Controls;

namespace WinVora
{
    public sealed partial class MainWindow
    {
        private async void Autostart_Click(object sender, RoutedEventArgs e)
        {
            SetPage("Autostart");
            await RenderAutostartPageAsync();
        }

        private async Task RenderAutostartPageAsync()
        {
            AutostartListPanel.Children.Clear();
            AutostartListPanel.Children.Add(new ProgressRing { IsActive = true, Width = 28, Height = 28, HorizontalAlignment = HorizontalAlignment.Center });
            SetGlobalStatus(Localization.CurrentLanguage == "en" ? "Loading startup programs..." : "Autostart-Programme werden geladen...");
            var entries = await Task.Run(AutostartService.GetEntries);
            AutostartListPanel.Children.Clear();
            SetGlobalStatus(null);
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
                bool revertingToggle = false;
                toggle.Toggled += (_, __) =>
                {
                    if (revertingToggle) return;
                    bool requestedState = toggle.IsOn;
                    try
                    {
                        if (!AutostartService.SetEnabled(entry, requestedState))
                        {
                            revertingToggle = true;
                            toggle.IsOn = !requestedState;
                            revertingToggle = false;
                            ShowInfo(Localization.CurrentLanguage == "en"
                                ? $"{entry.Name} could not be changed."
                                : $"{entry.Name} konnte nicht geändert werden.", InfoBarSeverity.Error);
                            return;
                        }
                        ScheduleDashboardRefresh();
                        ShowInfo(requestedState
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
                if (AutostartService.TryGetCommandTargetPath(entry.Command, out string startupPath) && File.Exists(startupPath))
                {
                    var openLocation = new Button
                    {
                        Content = Localization.CurrentLanguage == "en" ? "Open location" : "Speicherort öffnen",
                        Padding = new Thickness(10, 5, 10, 5)
                    };
                    openLocation.Click += (_, __) =>
                    {
                        var result = ExplorerService.SelectFile(startupPath);
                        if (result == ExplorerOpenResult.Missing)
                            ShowInfo(Localization.CurrentLanguage == "en" ? "The file no longer exists." : "Die Datei ist nicht mehr vorhanden.", InfoBarSeverity.Warning);
                    };
                    statusPanel.Children.Add(openLocation);
                }
                string pathLabel = Localization.CurrentLanguage == "en" ? "Path: " : "Pfad: ";
                string shortCommand = entry.Command.Length > 92 ? entry.Command[..89] + "..." : entry.Command;
                string baseDescription = pathLabel + shortCommand;
                var startupCard = new ToolkitControls.SettingsCard
                {
                    Header = entry.Name,
                    Description = baseDescription + " · " +
                                  (Localization.CurrentLanguage == "en" ? "Signature is being checked..." : "Signatur wird geprüft..."),
                    Content = statusPanel,
                    CornerRadius = new CornerRadius(16)
                };
                ToolTipService.SetToolTip(startupCard, entry.Command);
                AutostartListPanel.Children.Add(startupCard);
                _ = LoadAutostartIdentityAsync(startupCard, entry, baseDescription);
            }
            if (entries.Count == 0)
                AutostartListPanel.Children.Add(MakeEmptyState(
                    "\uE768",
                    Localization.CurrentLanguage == "en" ? "No startup programs" : "Keine Autostart-Programme",
                    Localization.CurrentLanguage == "en" ? "No programs start automatically for this user." : "Für diesen Benutzer starten keine Programme automatisch."));
        }

        private async Task LoadAutostartIdentityAsync(
            ToolkitControls.SettingsCard card,
            AutostartEntry entry,
            string baseDescription)
        {
            var identity = await Task.Run(() => AutostartService.GetFileIdentity(entry.Command));
            if (_currentPageKey != "Autostart") return;
            bool en = Localization.CurrentLanguage == "en";
            card.Description = baseDescription + " · " +
                               (en ? "Publisher: " : "Herausgeber: ") + identity.Publisher + " · " +
                               (identity.Signed ? (en ? "Signed" : "Signiert") : (en ? "Signature unknown" : "Signatur unbekannt"));
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
    }
}
