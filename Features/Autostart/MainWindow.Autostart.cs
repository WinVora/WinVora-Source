using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using ToolkitControls = CommunityToolkit.WinUI.Controls;

namespace WinVora
{
    public sealed partial class MainWindow
    {
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
                string pathLabel = Localization.CurrentLanguage == "en" ? "Path: " : "Pfad: ";
                string shortCommand = entry.Command.Length > 92 ? entry.Command[..89] + "..." : entry.Command;
                var startupCard = new ToolkitControls.SettingsCard
                {
                    Header = entry.Name,
                    Description = pathLabel + shortCommand,
                    Content = statusPanel,
                    CornerRadius = new CornerRadius(16)
                };
                ToolTipService.SetToolTip(startupCard, entry.Command);
                AutostartListPanel.Children.Add(startupCard);
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
    }
}
