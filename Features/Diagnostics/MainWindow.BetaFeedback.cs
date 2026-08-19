using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace WinVora
{
    public sealed partial class MainWindow
    {
        private const string BetaIssueUrl = "https://github.com/WinVora/WinVora-Source/issues/new";

        private void UpdateUpdateChannelUi()
        {
            if (UpdateChannelLabel == null) return;
            bool en = Localization.CurrentLanguage == "en";
            UpdateChannelLabel.Text = _settings.UpdateChannel == "Beta"
                ? (en ? "Channel: Beta" : "Kanal: Beta")
                : (en ? "Channel: Stable" : "Kanal: Stabil");
        }

        private void UpdateChannelButton_Click(object sender, RoutedEventArgs e)
        {
            SettingsButton_Click(sender, e);
        }

        private async Task OpenBetaFeedbackAsync(Window? owner = null)
        {
            bool en = Localization.CurrentLanguage == "en";
            SystemInfoSnapshot snapshot;
            try
            {
                snapshot = _cachedSnapshot ??
                    await SystemInfoProvider.GetFullSnapshotAsync(_startupCancellation.Token);
            }
            catch (Exception ex)
            {
                // Feedback muss auch dann funktionieren, wenn gerade eine WMI-
                // oder Systemabfrage ausfällt. Der Bericht enthält dann nur
                // die sicher verfügbaren Basisangaben.
                Logger.LogError("Systeminformationen für Beta-Feedback", ex);
                snapshot = new SystemInfoSnapshot
                {
                    WindowsEdition = Environment.OSVersion.Platform.ToString(),
                    WindowsVersion = Environment.OSVersion.VersionString,
                    Architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString()
                };
            }
            string log = await Task.Run(() => Logger.ReadForDiagnostics());
            string shortLog = DiagnosticReportBuilder.SelectRelevantLogLines(log);
            string report = DiagnosticReportBuilder.Build(snapshot, CurrentVersion, shortLog);
            var stepsBox = new TextBox
            {
                Header = en ? "Steps to reproduce" : "Schritte zum Nachstellen",
                PlaceholderText = en ? "1. Opened...\n2. Clicked..." : "1. Geöffnet...\n2. Geklickt...",
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 80
            };
            var expectedBox = new TextBox
            {
                Header = en ? "Expected behavior" : "Erwartetes Verhalten",
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 60
            };
            AutomationProperties.SetName(stepsBox, en ? "Steps to reproduce the beta problem" : "Schritte zum Nachstellen des Beta-Problems");
            AutomationProperties.SetName(expectedBox, en ? "Expected beta behavior" : "Erwartetes Verhalten der Beta");

            FrameworkElement? ownerContent = owner?.Content as FrameworkElement;
            var preview = new ContentDialog
            {
                XamlRoot = ownerContent?.XamlRoot ?? RootGrid.XamlRoot,
                Title = en ? "Prepare beta feedback" : "Beta-Feedback vorbereiten",
                Content = new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = en
                                ? "Review the anonymized information below. GitHub opens a draft; you decide whether to submit it."
                                : "Prüfe die anonymisierten Angaben. GitHub öffnet nur einen Entwurf; du entscheidest selbst, ob du ihn absendest.",
                            TextWrapping = TextWrapping.Wrap
                        },
                        stepsBox,
                        expectedBox,
                        new TextBox
                        {
                            Text = report,
                            IsReadOnly = true,
                            AcceptsReturn = true,
                            TextWrapping = TextWrapping.NoWrap,
                            MinWidth = 500,
                            MaxHeight = 320
                        }
                    }
                },
                PrimaryButtonText = en ? "Open GitHub draft" : "GitHub-Entwurf öffnen",
                CloseButtonText = en ? "Cancel" : "Abbrechen",
                DefaultButton = ContentDialogButton.Close
            };
            if (await preview.ShowAsync() != ContentDialogResult.Primary) return;

            string issueBody = (en
                ? $"## Problem\n\nDescribe what happened.\n\n## Steps to reproduce\n\n{stepsBox.Text}\n\n## Expected behavior\n\n{expectedBox.Text}\n\n## Anonymized diagnostics\n\n```text\n"
                : $"## Problem\n\nBeschreibe, was passiert ist.\n\n## Schritte zum Nachstellen\n\n{stepsBox.Text}\n\n## Erwartetes Verhalten\n\n{expectedBox.Text}\n\n## Anonymisierte Diagnoseinformationen\n\n```text\n")
                + report + "\n```";
            string url = $"{BetaIssueUrl}?labels=bug,beta&title={Uri.EscapeDataString($"[Beta {CurrentVersion}] ")}&body={Uri.EscapeDataString(issueBody)}";
            try
            {
                bool opened = await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
                if (!opened)
                    ShowInfo(en ? "GitHub could not be opened." : "GitHub konnte nicht geöffnet werden.", InfoBarSeverity.Error);
            }
            catch (Exception ex)
            {
                Logger.LogError("GitHub-Feedback öffnen", ex);
                ShowInfo(en ? "GitHub could not be opened." : "GitHub konnte nicht geöffnet werden.", InfoBarSeverity.Error);
            }
        }
    }
}
