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
            // Schutz gegen versehentliches mehrfaches Initialisieren: Eine
            // bereits umgebaute große Karte erhält keinen zweiten Button.
            if (card.Tag as string == "SystemInfoCopyAttached") return;
            if (card.Content is not FrameworkElement originalContent) return;

            var button = Create(getText);
            button.HorizontalAlignment = HorizontalAlignment.Right;
            button.VerticalAlignment = VerticalAlignment.Top;
            button.Margin = new Thickness(0, 0, 0, 0);

            // Direkt in den Content der SettingsCard einsetzen. Eine äußere
            // Überlagerung wurde vom Control-Template verdeckt und war deshalb
            // nicht sichtbar.
            var wrapper = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
            originalContent.Margin = new Thickness(0, 0, 116, 0);
            wrapper.Children.Add(originalContent);
            wrapper.Children.Add(button);
            card.Content = wrapper;
            card.Tag = "SystemInfoCopyAttached";
        }

        public static Button Create(Func<string> getText)
        {
            bool en = Localization.CurrentLanguage == "en";
            var button = new Button
            {
                Content = CreateContent(en ? "Copy" : "Kopieren", "\uE8C8"),
                Width = 104,
                Height = 32,
                Padding = new Thickness(10, 0, 10, 0),
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
                button.Content = CreateContent(en ? "Copied" : "Kopiert", "\uE73E");
                var resetTimer = button.DispatcherQueue.CreateTimer();
                resetTimer.Interval = TimeSpan.FromSeconds(1.4);
                resetTimer.Tick += (_, __) =>
                {
                    resetTimer.Stop();
                    button.Content = CreateContent(en ? "Copy" : "Kopieren", "\uE8C8");
                    ToolTipService.SetToolTip(button, en ? "Copy information" : "Informationen kopieren");
                };
                resetTimer.Start();
            };
            return button;
        }

        private static StackPanel CreateContent(string label, string glyph) => new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                new FontIcon { Glyph = glyph, FontSize = 13 },
                new TextBlock { Text = label, FontSize = 12 }
            }
        };

    }
}
