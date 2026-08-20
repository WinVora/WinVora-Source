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
            if (!TrySetPage("Autostart")) return;
            await RenderAutostartPageAsync();
        }

        private async Task RenderAutostartPageAsync()
        {
            AutostartListPanel.Children.Clear();
            AutostartListPanel.Children.Add(new ProgressRing { IsActive = true, Width = 28, Height = 28, HorizontalAlignment = HorizontalAlignment.Center });
            SetGlobalStatus(Localization.T("Autostart.Loading"));
            var entries = await Task.Run(AutostartService.GetEntries);
            AutostartListPanel.Children.Clear();
            SetGlobalStatus(null);
            PageSubtitle.Text = Localization.F("Autostart.Count", entries.Count);
            foreach (var entry in entries)
            {
                bool targetExists = AutostartService.CommandTargetExists(entry.Command);
                var toggle = new ToggleSwitch
                {
                    IsOn = entry.Enabled,
                    OnContent = Localization.T("Autostart.Active"),
                    OffContent = Localization.T("Autostart.Disabled")
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
                            ShowInfo(Localization.F("Autostart.ChangeFailed", entry.Name), InfoBarSeverity.Error);
                            return;
                        }
                        ScheduleDashboardRefresh();
                        ShowInfo(Localization.F(requestedState ? "Autostart.EnabledMessage" : "Autostart.DisabledMessage", entry.Name), InfoBarSeverity.Success);
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
                        Text = targetExists ? "OK" : Localization.T("Autostart.FileMissing"),
                        Foreground = new SolidColorBrush(targetExists
                            ? Windows.UI.Color.FromArgb(0xFF, 0x4C, 0xD9, 0x73)
                            : Windows.UI.Color.FromArgb(0xFF, 0xFF, 0x6B, 0x6B))
                    }
                });
                statusPanel.Children.Add(toggle);
                if (!targetExists)
                {
                    var removeMissing = new Button
                    {
                        Content = Localization.T("Autostart.RemoveMissing"),
                        Padding = new Thickness(10, 5, 10, 5)
                    };
                    removeMissing.Click += async (_, __) =>
                    {
                        bool confirmed = await ConfirmAsync(
                            Localization.T("Autostart.RemoveTitle"),
                            Localization.F("Autostart.RemoveMessage", entry.Name),
                            Localization.T("Autostart.RemoveMissing"),
                            respectDeleteConfirmationSetting: false);
                        if (!confirmed) return;
                        if (!AutostartService.RemoveEntry(entry))
                        {
                            ShowInfo(Localization.T("Autostart.RemoveFailed"), InfoBarSeverity.Error);
                            return;
                        }
                        ShowInfo(Localization.T("Autostart.Removed"), InfoBarSeverity.Success);
                        ScheduleDashboardRefresh();
                        await RenderAutostartPageAsync();
                    };
                    statusPanel.Children.Add(removeMissing);
                }
                if (AutostartService.TryGetCommandTargetPath(entry.Command, out string startupPath) && File.Exists(startupPath))
                {
                    var openLocation = new Button
                    {
                        Content = Localization.T("Autostart.OpenLocation"),
                        Padding = new Thickness(10, 5, 10, 5)
                    };
                    openLocation.Click += (_, __) =>
                    {
                        var result = ExplorerService.SelectFile(startupPath);
                        if (result == ExplorerOpenResult.Missing)
                            ShowInfo(Localization.T("Autostart.FileGone"), InfoBarSeverity.Warning);
                    };
                    statusPanel.Children.Add(openLocation);
                }
                string pathLabel = Localization.T("Autostart.Path");
                string shortCommand = entry.Command.Length > 92 ? entry.Command[..89] + "..." : entry.Command;
                string baseDescription = pathLabel + shortCommand;
                var startupCard = new ToolkitControls.SettingsCard
                {
                    Header = entry.Name,
                    Description = baseDescription + " · " +
                                  Localization.T("Autostart.SignatureChecking"),
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
                    Localization.T("Autostart.EmptyTitle"),
                    Localization.T("Autostart.EmptyDescription")));
        }

        private async Task LoadAutostartIdentityAsync(
            ToolkitControls.SettingsCard card,
            AutostartEntry entry,
            string baseDescription)
        {
            var identity = await Task.Run(() => AutostartService.GetFileIdentity(entry.Command));
            if (_currentPageKey != "Autostart") return;
            card.Description = baseDescription + " · " +
                               Localization.T("Autostart.Publisher") + identity.Publisher + " · " +
                               Localization.T(identity.Signed ? "Autostart.Signed" : "Autostart.SignatureUnknown");
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
