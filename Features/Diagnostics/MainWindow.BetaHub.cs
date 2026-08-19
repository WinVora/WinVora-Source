using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage.Pickers;

namespace WinVora
{
    public sealed partial class MainWindow
    {
        private Window? _betaHubWindow;

        private async Task ShowBetaHubAsync()
        {
            if (_betaHubWindow != null)
            {
                _betaHubWindow.Activate();
                WindowActivationService.ShowOwnedInFront(this, _betaHubWindow);
                return;
            }

            bool en = Localization.CurrentLanguage == "en";
            var window = _betaHubWindow = new Window { Title = en ? "WinVora Beta Center" : "WinVora Beta-Zentrale" };
            window.Closed += (_, __) => _betaHubWindow = null;
            var panel = new StackPanel { Spacing = 14, Padding = new Thickness(24) };
            panel.Children.Add(new TextBlock { Text = en ? "Beta Center" : "Beta-Zentrale", FontSize = 28, FontWeight = Microsoft.UI.Text.FontWeights.Bold });
            panel.Children.Add(new TextBlock
            {
                Text = $"{CurrentVersion} · " + (en ? "Feedback and beta settings" : "Feedback und Beta-Einstellungen"),
                Foreground = (SolidColorBrush)RootGrid.Resources["AppMutedForegroundBrush"]
            });

            var feedback = new Button
            {
                Content = en ? "Report a problem" : "Problem melden",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Style = (Style)Application.Current.Resources["AccentButtonStyle"]
            };
            feedback.Click += async (_, __) => await OpenBetaFeedbackAsync(window);
            panel.Children.Add(feedback);

            var issuesLink = new Button { Content = en ? "View known issues on GitHub" : "Bekannte Probleme auf GitHub ansehen", HorizontalAlignment = HorizontalAlignment.Stretch };
            issuesLink.Click += async (_, __) => await Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/WinVora/WinVora-Source/issues"));
            panel.Children.Add(issuesLink);

            var diagnosticZip = new Button { Content = en ? "Save anonymized diagnostic ZIP" : "Anonymisierte Diagnose als ZIP speichern", HorizontalAlignment = HorizontalAlignment.Stretch };
            diagnosticZip.Click += async (_, __) => await ExportDiagnosticReportAsync(window);
            panel.Children.Add(diagnosticZip);

            panel.Children.Add(new TextBlock { Text = en ? "Watched folders" : "Beobachtete Ordner", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
            var watchedPanel = new StackPanel { Spacing = 6 };
            void RefreshWatched()
            {
                watchedPanel.Children.Clear();
                foreach (string path in _settings.WatchedFolders)
                {
                    var row = new Grid { ColumnSpacing = 8 };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.Children.Add(new TextBlock { Text = path, TextTrimming = TextTrimming.CharacterEllipsis });
                    var remove = new Button { Content = en ? "Remove" : "Entfernen" };
                    remove.Click += (_, __) => { _settings.WatchedFolders.Remove(path); _settings.Save(); RefreshWatched(); };
                    Grid.SetColumn(remove, 1); row.Children.Add(remove);
                    watchedPanel.Children.Add(row);
                }
            }
            RefreshWatched();
            panel.Children.Add(watchedPanel);
            var addFolder = new Button { Content = en ? "Add folder" : "Ordner hinzufügen", HorizontalAlignment = HorizontalAlignment.Left };
            addFolder.Click += async (_, __) =>
            {
                var picker = new FolderPicker();
                picker.FileTypeFilter.Add("*");
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));
                var folder = await picker.PickSingleFolderAsync();
                if (folder != null && !_settings.WatchedFolders.Contains(folder.Path, StringComparer.OrdinalIgnoreCase))
                { _settings.WatchedFolders.Add(folder.Path); _settings.Save(); RefreshWatched(); }
            };
            panel.Children.Add(addFolder);

            window.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            StyleDarkWindow(window, 620, 610);
            WindowActivationService.PlaceWindow(this, window, null, null, 620, 610);
            window.Activate();
            WindowActivationService.ShowOwnedInFront(this, window);
        }
    }
}
