using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System;

namespace WinVora
{
    internal static class LoadingStateUiBuilder
    {
        public static StackPanel Create(ResourceDictionary resources, int rows = 3, bool animate = true)
        {
            var panel = new StackPanel { Spacing = UiMetrics.SpaceMd };
            for (int index = 0; index < rows; index++)
            {
                var placeholder = new Border
                {
                    Height = 78,
                    CornerRadius = new CornerRadius(UiMetrics.CardRadius),
                    Background = (SolidColorBrush)resources["AppCardSurfaceBrush"],
                    Opacity = 0.48
                };
                var pulse = new DoubleAnimation
                {
                    From = 0.42,
                    To = 0.82,
                    Duration = TimeSpan.FromMilliseconds(700),
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever
                };
                Storyboard.SetTarget(pulse, placeholder);
                Storyboard.SetTargetProperty(pulse, "Opacity");
                var storyboard = new Storyboard();
                storyboard.Children.Add(pulse);
                if (animate)
                {
                    placeholder.Loaded += (_, __) => storyboard.Begin();
                    placeholder.Unloaded += (_, __) => storyboard.Stop();
                }
                panel.Children.Add(placeholder);
            }
            return panel;
        }
    }
}
