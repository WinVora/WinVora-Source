using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using Windows.ApplicationModel.DataTransfer;
using ToolkitControls = CommunityToolkit.WinUI.Controls;

namespace WinVora
{
    internal static class SystemInfoCopyButton
    {
        public static void Attach(ToolkitControls.SettingsCard card, Func<string> getText)
        {
            if (card.Content is not FrameworkElement originalContent) return;

            var button = Create(getText);
            button.HorizontalAlignment = HorizontalAlignment.Right;
            button.Margin = new Thickness(0, 0, 0, 10);

            // Direkt in den Content der SettingsCard einsetzen. Eine äußere
            // Überlagerung wurde vom Control-Template verdeckt und war deshalb
            // nicht sichtbar.
            var wrapper = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
            wrapper.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            wrapper.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(originalContent, 1);
            wrapper.Children.Add(button);
            wrapper.Children.Add(originalContent);
            card.Content = wrapper;
        }

        public static Button Create(Func<string> getText)
        {
            bool en = Localization.CurrentLanguage == "en";
            var button = new Button
            {
                Content = new FontIcon { Glyph = "\uE8C8", FontSize = 14 },
                Width = 36,
                Height = 32,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(8)
            };
            ToolTipService.SetToolTip(button, en ? "Copy information" : "Informationen kopieren");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
                button, en ? "Copy information" : "Informationen kopieren");

            button.Click += (_, __) =>
            {
                string text = getText();
                if (string.IsNullOrWhiteSpace(text)) return;

                var package = new DataPackage();
                package.SetText(text);
                Clipboard.SetContent(package);
                Clipboard.Flush();
                ToolTipService.SetToolTip(button, en ? "Copied" : "Kopiert");

                // Sichtbares Feedback, auch wenn kein Tooltip geöffnet ist.
                button.Content = new FontIcon { Glyph = "\uE73E", FontSize = 14 };
                var resetTimer = button.DispatcherQueue.CreateTimer();
                resetTimer.Interval = TimeSpan.FromSeconds(1.4);
                resetTimer.Tick += (_, __) =>
                {
                    resetTimer.Stop();
                    button.Content = new FontIcon { Glyph = "\uE8C8", FontSize = 14 };
                    ToolTipService.SetToolTip(button, en ? "Copy information" : "Informationen kopieren");
                };
                resetTimer.Start();
            };
            return button;
        }

    }
}
