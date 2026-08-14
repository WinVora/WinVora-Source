using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace WinVora
{
    internal static class SettingsUiBuilder
    {
        public static Border CreateSection(string title, ResourceDictionary resources, out StackPanel content)
        {
            content = new StackPanel { Spacing = UiMetrics.SpaceLg };
            content.Children.Add(new TextBlock
            {
                Text = title,
                Style = (Style)Application.Current.Resources["WinVoraSectionTitleStyle"],
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (SolidColorBrush)resources["AppForegroundBrush"]
            });
            var card = CommonUiBuilder.CreateCard(content, resources);
            card.Tag = title;
            return card;
        }

        public static StackPanel CreateLabeledControl(string label, FrameworkElement control, ResourceDictionary resources)
        {
            var panel = new StackPanel { Spacing = UiMetrics.SpaceSm };
            panel.Children.Add(new TextBlock
            {
                Text = label,
                Style = (Style)Application.Current.Resources["WinVoraBodyTextStyle"],
                Foreground = (SolidColorBrush)resources["AppForegroundBrush"]
            });
            panel.Children.Add(control);
            return panel;
        }
    }
}
