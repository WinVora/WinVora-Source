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
            if (card.Tag as string == "SystemInfoCopyAttached") return;

            bool en = Localization.CurrentLanguage == "en";
            card.ActionIcon = new FontIcon { Glyph = "\uE8C8", FontSize = 15 };
            card.ActionIconToolTip = en ? "Copy information" : "Informationen kopieren";
            card.IsActionIconVisible = true;
            card.IsClickEnabled = true;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
                card, en ? "Copy system information" : "Systeminformationen kopieren");

            card.Click += (_, __) =>
            {
                string text = getText();
                if (string.IsNullOrWhiteSpace(text)) return;

                var package = new DataPackage();
                package.SetText(text);
                Clipboard.SetContent(package);
                Clipboard.Flush();

                card.ActionIcon = new FontIcon { Glyph = "\uE73E", FontSize = 15 };
                card.ActionIconToolTip = en ? "Copied" : "Kopiert";
                var resetTimer = card.DispatcherQueue.CreateTimer();
                resetTimer.Interval = TimeSpan.FromSeconds(1.4);
                resetTimer.Tick += (_, __) =>
                {
                    resetTimer.Stop();
                    card.ActionIcon = new FontIcon { Glyph = "\uE8C8", FontSize = 15 };
                    card.ActionIconToolTip = en ? "Copy information" : "Informationen kopieren";
                };
                resetTimer.Start();
            };

            card.Tag = "SystemInfoCopyAttached";
        }

    }
}
