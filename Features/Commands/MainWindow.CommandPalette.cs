using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace WinVora
{
    public sealed partial class MainWindow
    {
        private async Task ShowCommandPaletteAsync()
        {
            bool en = Localization.CurrentLanguage == "en";
            ContentDialog? dialog = null;
            var results = new StackPanel { Spacing = 6 };
            var search = new TextBox
            {
                PlaceholderText = en ? "Type a command..." : "Befehl suchen...",
                Height = 42
            };
            var commands = new List<(string Key, string Glyph, string De, string En, string Shortcut, Func<Task> Run)>
            {
                ("dashboard", "\uE80F", "Dashboard öffnen", "Open dashboard", "", () => { TrySetPage("Übersicht"); return Task.CompletedTask; }),
                ("system", "\uE950", "Systeminformationen öffnen", "Open system information", "", () => { TrySetPage("System"); return Task.CompletedTask; }),
                ("updates", "\uE895", "Programm-Updates prüfen", "Check program updates", "Strg+R", async () => { if (TrySetPage("Updates")) await LoadWinget(forceRefresh: true); }),
                ("storage", "\uE8B7", "Dateien analysieren", "Analyze storage", "", async () => { if (TrySetPage("Storage")) await LoadStorage(); }),
                ("programs", "\uE74D", "Programme anzeigen", "Show installed programs", "", async () => { if (TrySetPage("Uninstall")) await LoadInstalledPrograms(); }),
                ("changes", "\uE9D2", "Veränderungen öffnen", "Open PC changes", "", () => { TrySetPage("Changes"); return Task.CompletedTask; }),
                ("performance", "\uE9D9", "PC-Check starten", "Run PC Check", "", async () => { if (TrySetPage("Performance")) await AnalyzePerformanceAsync(); }),
                ("history", "\uE81C", "Verlauf öffnen", "Open history", "", () => { if (TrySetPage("History")) RenderHistoryPage(); return Task.CompletedTask; }),
                ("settings", "\uE713", "Einstellungen öffnen", "Open settings", "", () => { SettingsButton_Click(this, new RoutedEventArgs()); return Task.CompletedTask; }),
                ("diagnostics", "\uE9D9", "WinVora-Status prüfen", "Check WinVora status", "", async () => await ShowInternalDiagnosticsAsync())
                ,("support-report", "\uE8A5", "Diagnosebericht erstellen", "Create diagnostic report", "", async () => await ExportDiagnosticReportAsync(this))
                ,("program-export", "\uE74E", "Programmliste exportieren", "Export program list", "", () => { UninstallExportTxt_Click(this, new RoutedEventArgs()); return Task.CompletedTask; })
            };

            void Render(string query)
            {
                results.Children.Clear();
                var filtered = commands.Where(command =>
                    (en ? command.En : command.De).Contains(query, StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrWhiteSpace(query))
                    filtered = filtered.OrderBy(command =>
                    {
                        int index = _settings.RecentCommands.FindIndex(key => key.Equals(command.Key, StringComparison.OrdinalIgnoreCase));
                        return index < 0 ? int.MaxValue : index;
                    });
                foreach (var command in filtered)
                {
                    var contentGrid = new Grid { ColumnSpacing = 10 };
                    contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    contentGrid.Children.Add(new FontIcon { Glyph = command.Glyph, FontSize = 15 });
                    var label = new TextBlock { Text = en ? command.En : command.De, VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(label, 1); contentGrid.Children.Add(label);
                    var shortcut = new TextBlock { Text = command.Shortcut, Opacity = 0.65, VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(shortcut, 2); contentGrid.Children.Add(shortcut);
                    var button = new Button
                    {
                        Content = contentGrid,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                        Padding = new Thickness(12, 9, 12, 9)
                    };
                    button.Click += async (_, __) =>
                    {
                        _settings.RecentCommands.RemoveAll(key => key.Equals(command.Key, StringComparison.OrdinalIgnoreCase));
                        _settings.RecentCommands.Insert(0, command.Key);
                        while (_settings.RecentCommands.Count > 5) _settings.RecentCommands.RemoveAt(5);
                        _settings.Save();
                        dialog?.Hide();
                        await command.Run();
                    };
                    results.Children.Add(button);
                }
            }

            search.TextChanged += (_, __) => Render(search.Text.Trim());
            Render(string.Empty);
            var content = new StackPanel { Spacing = 10, Width = 440 };
            content.Children.Add(search);
            content.Children.Add(new ScrollViewer { Content = results, MaxHeight = 380 });
            dialog = CommonUiBuilder.CreateConfirmation(
                RootGrid.XamlRoot,
                en ? "Commands" : "Befehle",
                content,
                null,
                en ? "Close" : "Schließen");
            dialog.Opened += (_, __) => search.Focus(FocusState.Keyboard);
            await dialog.ShowAsync();
        }
    }
}
