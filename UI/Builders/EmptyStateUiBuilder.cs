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
                Spacing = UiMetrics.SpaceMd, HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(UiMetrics.CardPadding)
            };
            panel.Children.Add(new FontIcon
            {
                Glyph = glyph, FontSize = 30,
                Foreground = (SolidColorBrush)resources["AppAccentBrushLight"]
            });
            panel.Children.Add(new TextBlock
            {
                Text = title,
                Style = (Style)Application.Current.Resources["WinVoraSectionTitleStyle"],
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
                MinHeight = 180,
                CornerRadius = new CornerRadius(UiMetrics.CardRadius),
                Padding = new Thickness(UiMetrics.CardPadding),
                Background = (SolidColorBrush)resources["AppCardSurfaceBrush"],
                BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0), Child = panel
            };
        }
    }
}
