using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Diagnostics;

namespace WinVora
{
    internal static class PcChangesUiBuilder
    {
        public static Border CreateMetric(
            string title, string value, string glyph, string color,
            Brush foreground, Brush mutedForeground, Brush background, Brush border)
        {
            var accent = new SolidColorBrush(ParseColor(color));
            var content = new StackPanel { Spacing = 10 };
            content.Children.Add(new FontIcon
            {
                Glyph = glyph, FontSize = 18, Foreground = accent,
                HorizontalAlignment = HorizontalAlignment.Left
            });
            content.Children.Add(new TextBlock
            {
                Text = value, FontSize = 28, FontWeight = FontWeights.Bold, Foreground = foreground
            });
            content.Children.Add(new TextBlock { Text = title, FontSize = 13, Foreground = mutedForeground });
            return new Border
            {
                CornerRadius = new CornerRadius(16), Padding = new Thickness(18), MinHeight = 118,
                Background = background, BorderBrush = border, BorderThickness = new Thickness(1), Child = content
            };
        }

        public static Border CreateStorageWarning(
            StorageGrowth growth, bool english, Brush background, Brush mutedForeground, Action<string> openFolder)
        {
            var openButton = new Button { Content = english ? "Open folder" : "Ordner öffnen" };
            openButton.Click += (_, __) => openFolder(growth.Path);
            var warningColor = Windows.UI.Color.FromArgb(255, 245, 185, 66);
            var warningIcon = new Border
            {
                Width = 40, Height = 40, CornerRadius = new CornerRadius(20),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(35, 245, 185, 66)),
                Child = new FontIcon { Glyph = "\uE7BA", Foreground = new SolidColorBrush(warningColor) }
            };
            var text = new StackPanel { Spacing = 3 };
            text.Children.Add(new TextBlock { Text = growth.Name, FontSize = 16, FontWeight = FontWeights.SemiBold });
            text.Children.Add(new TextBlock
            {
                Text = english
                    ? $"+{StorageService.FormatBytes(growth.GrowthBytes)} since last check · {StorageService.FormatBytes(growth.CurrentBytes)} total"
                    : $"+{StorageService.FormatBytes(growth.GrowthBytes)} seit letzter Prüfung · {StorageService.FormatBytes(growth.CurrentBytes)} insgesamt",
                Foreground = mutedForeground, TextWrapping = TextWrapping.Wrap
            });
            var layout = new Grid { ColumnSpacing = 14 };
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(text, 1);
            Grid.SetColumn(openButton, 2);
            layout.Children.Add(warningIcon);
            layout.Children.Add(text);
            layout.Children.Add(openButton);
            return new Border
            {
                CornerRadius = new CornerRadius(16), Padding = new Thickness(18), Background = background,
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(100, 245, 185, 66)),
                BorderThickness = new Thickness(1), Child = layout
            };
        }

        private static Windows.UI.Color ParseColor(string value)
        {
            string hex = value.TrimStart('#');
            return Windows.UI.Color.FromArgb(255,
                Convert.ToByte(hex[..2], 16), Convert.ToByte(hex.Substring(2, 2), 16),
                Convert.ToByte(hex.Substring(4, 2), 16));
        }
    }
}
