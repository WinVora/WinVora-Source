using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace WinVora
{
    internal static class CommonUiBuilder
    {
        public static Border CreateCard(UIElement child, ResourceDictionary resources,
            double padding = 20, double cornerRadius = 16)
        {
            return new Border
            {
                CornerRadius = new CornerRadius(cornerRadius),
                Padding = new Thickness(padding),
                Background = (SolidColorBrush)resources["AppOverlay18"],
                BorderBrush = (SolidColorBrush)resources["AppOverlay28"],
                BorderThickness = new Thickness(1),
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
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(8, 4, 8, 4),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(
                    0x24, foreground.Color.R, foreground.Color.G, foreground.Color.B)),
                Child = new TextBlock { Text = text, FontSize = 11, Foreground = foreground }
            };
        }

        public static ContentDialog CreateConfirmation(XamlRoot xamlRoot, string title,
            object content, string? primaryText, string closeText)
        {
            return new ContentDialog
            {
                XamlRoot = xamlRoot,
                Title = title,
                Content = content,
                PrimaryButtonText = primaryText,
                CloseButtonText = closeText,
                DefaultButton = ContentDialogButton.Close
            };
        }
    }
}
