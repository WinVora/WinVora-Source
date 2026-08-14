using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;

namespace WinVora
{
    public sealed partial class MainWindow
    {
        private async void DashboardCustomizeHeaderButton_Click(object sender, RoutedEventArgs e)
        {
            await ShowDashboardCustomizationAsync();
        }

        private async Task ShowDashboardCustomizationAsync()
        {
            bool en = Localization.CurrentLanguage == "en";
            var labels = new Dictionary<string, (string De, string En)>
            {
                ["Updates"] = ("Updates", "Updates"),
                ["Security"] = ("Sicherheit", "Security"),
                ["Storage"] = ("Speicherplatz", "Storage"),
                ["Cpu"] = ("CPU", "CPU"),
                ["Ram"] = ("RAM", "RAM"),
                ["Gpu"] = ("GPU", "GPU")
            };
            var panel = new StackPanel { Spacing = 8, Width = 460 };

            void Render()
            {
                panel.Children.Clear();
                panel.Children.Add(new TextBlock
                {
                    Text = en ? "Drag cards to change their order." : "Ziehe Karten, um ihre Reihenfolge zu ändern.",
                    Foreground = (Microsoft.UI.Xaml.Media.SolidColorBrush)RootGrid.Resources["AppMutedForegroundBrush"],
                    FontSize = 12
                });
                foreach (string key in _settings.DashboardCardOrder.Where(labels.ContainsKey))
                {
                    var row = new Grid
                    {
                        ColumnSpacing = 8,
                        Padding = new Thickness(8, 6, 8, 6),
                        CornerRadius = new CornerRadius(8),
                        Background = (Microsoft.UI.Xaml.Media.SolidColorBrush)RootGrid.Resources["AppOverlay10"],
                        CanDrag = true,
                        AllowDrop = true,
                        Tag = key
                    };
                    row.DragStarting += (_, args) =>
                    {
                        args.Data.SetText(key);
                        args.Data.RequestedOperation = DataPackageOperation.Move;
                    };
                    row.DragOver += (_, args) =>
                    {
                        args.AcceptedOperation = DataPackageOperation.Move;
                        args.DragUIOverride.Caption = en ? "Move card" : "Karte verschieben";
                    };
                    row.Drop += async (_, args) =>
                    {
                        if (!args.DataView.Contains(StandardDataFormats.Text)) return;
                        string sourceKey = await args.DataView.GetTextAsync();
                        int source = _settings.DashboardCardOrder.IndexOf(sourceKey);
                        int target = _settings.DashboardCardOrder.IndexOf(key);
                        if (source < 0 || target < 0 || source == target) return;
                        _settings.DashboardCardOrder.RemoveAt(source);
                        _settings.DashboardCardOrder.Insert(target, sourceKey);
                        _settings.Save();
                        ApplyDashboardCustomizationLayout();
                        Render();
                    };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var grip = new FontIcon
                    {
                        Glyph = "\uE700",
                        FontSize = 14,
                        Opacity = 0.65,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    ToolTipService.SetToolTip(grip, en ? "Drag to reorder" : "Ziehen zum Sortieren");
                    var visible = new CheckBox
                    {
                        IsChecked = !_settings.HiddenDashboardCards.Contains(key, StringComparer.OrdinalIgnoreCase),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(visible, 1);
                    visible.Click += (_, __) =>
                    {
                        _settings.HiddenDashboardCards.RemoveAll(value => value.Equals(key, StringComparison.OrdinalIgnoreCase));
                        if (visible.IsChecked != true) _settings.HiddenDashboardCards.Add(key);
                        _settings.Save();
                        ApplyDashboardCustomizationLayout();
                    };
                    var label = new TextBlock
                    {
                        Text = en ? labels[key].En : labels[key].De,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(label, 2);
                    var up = new Button { Content = "↑", Width = 36, Height = 32, Padding = new Thickness(0) };
                    var down = new Button { Content = "↓", Width = 36, Height = 32, Padding = new Thickness(0) };
                    void Move(int direction)
                    {
                        int index = _settings.DashboardCardOrder.IndexOf(key);
                        int target = Math.Clamp(index + direction, 0, _settings.DashboardCardOrder.Count - 1);
                        if (index == target) return;
                        (_settings.DashboardCardOrder[index], _settings.DashboardCardOrder[target]) =
                            (_settings.DashboardCardOrder[target], _settings.DashboardCardOrder[index]);
                        _settings.Save();
                        ApplyDashboardCustomizationLayout();
                        Render();
                    }
                    up.Click += (_, __) => Move(-1);
                    down.Click += (_, __) => Move(1);
                    Grid.SetColumn(up, 3); Grid.SetColumn(down, 4);
                    row.Children.Add(grip); row.Children.Add(visible); row.Children.Add(label); row.Children.Add(up); row.Children.Add(down);
                    panel.Children.Add(row);
                }
            }
            Render();
            var dialog = new ContentDialog
            {
                XamlRoot = RootGrid.XamlRoot,
                Title = en ? "Customize dashboard" : "Dashboard anpassen",
                Content = panel,
                PrimaryButtonText = en ? "Restore defaults" : "Standard wiederherstellen",
                CloseButtonText = en ? "Done" : "Fertig",
                DefaultButton = ContentDialogButton.Close
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                _settings.HiddenDashboardCards.Clear();
                _settings.DashboardCardOrder = new List<string> { "Updates", "Security", "Storage", "Cpu", "Ram", "Gpu" };
                _settings.Save();
                ApplyDashboardCustomizationLayout();
            }
        }
    }
}
