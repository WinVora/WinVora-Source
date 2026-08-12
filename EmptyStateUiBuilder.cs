using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Threading.Tasks;

namespace WinVora
{
    internal static class EmptyStateUiBuilder
    {
        public static Border Create(string glyph, string title, string description,
            ResourceDictionary resources, string? actionText = null, Func<Task>? action = null)
        {
            var panel = new StackPanel
            {
                Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(20)
            };
            panel.Children.Add(new FontIcon
            {
                Glyph = glyph, FontSize = 30,
                Foreground = (SolidColorBrush)resources["AppAccentBrushLight"]
            });
            panel.Children.Add(new TextBlock
            {
                Text = title, FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            panel.Children.Add(new TextBlock
            {
                Text = description, Foreground = (SolidColorBrush)resources["AppFaintForegroundBrush"],
                TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap
            });
            if (action != null && !string.IsNullOrWhiteSpace(actionText))
            {
                var button = new Button { Content = actionText, HorizontalAlignment = HorizontalAlignment.Center };
                button.Click += async (_, __) => await action();
                panel.Children.Add(button);
            }
            return new Border
            {
                MinHeight = 180, CornerRadius = new CornerRadius(16), Padding = new Thickness(20),
                Background = (SolidColorBrush)resources["AppOverlay10"],
                BorderBrush = (SolidColorBrush)resources["AppOverlay22"],
                BorderThickness = new Thickness(1), Child = panel
            };
        }
    }
}
