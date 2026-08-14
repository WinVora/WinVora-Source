using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace WinVora
{
    internal static class CommonUiBuilder
    {
        public static Border CreateCard(UIElement child, ResourceDictionary resources,
            double padding = UiMetrics.CardPadding, double cornerRadius = UiMetrics.CardRadius)
        {
            return new Border
            {
                CornerRadius = new CornerRadius(cornerRadius),
                Padding = new Thickness(padding),
                Background = (SolidColorBrush)resources["AppCardSurfaceBrush"],
                BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                Child = child
            };
        }

        public static Border CreateStatusBadge(string text, bool warning, ResourceDictionary resources)
        {
            var foreground = warning
                ? (SolidColorBrush)resources["AppWarningBrush"]
                : (SolidColorBrush)resources["AppSuccessBrush"];
            return new Border
            {
                CornerRadius = new CornerRadius(UiMetrics.ControlRadius),
                Padding = new Thickness(UiMetrics.SpaceSm, UiMetrics.SpaceXs,
                    UiMetrics.SpaceSm, UiMetrics.SpaceXs),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(
                    0x24, foreground.Color.R, foreground.Color.G, foreground.Color.B)),
                Child = new TextBlock
                {
                    Text = text,
                    FontSize = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = foreground
                }
            };
        }

        public static ContentDialog CreateConfirmation(XamlRoot xamlRoot, string title,
            object content, string? primaryText, string closeText)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = xamlRoot,
                Title = title,
                Content = content,
                PrimaryButtonText = primaryText,
                CloseButtonText = closeText,
                DefaultButton = ContentDialogButton.Close
            };
            dialog.Resources["ContentDialogMinWidth"] = 420d;
            dialog.Resources["ContentDialogMaxWidth"] = 640d;
            return dialog;
        }
    }
}
