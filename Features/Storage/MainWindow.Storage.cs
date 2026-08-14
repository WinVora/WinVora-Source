using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using ToolkitControls = CommunityToolkit.WinUI.Controls;

namespace WinVora
{
    public sealed partial class MainWindow
    {
        // ================= STORAGE =================

        private async void Cleaner_Click(object sender, RoutedEventArgs e)
        {
            SetPage("Storage");
            await LoadStorage();
            ScheduleDashboardRefresh();
        }

        private async void StorageRefresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadStorage();
            ScheduleDashboardRefresh();
        }

        private void StorageSelectAll_Click(object sender, RoutedEventArgs e)
        {
            if (_storageRows.Count == 0) return;

            bool allSelected = _storageRows.All(r => r.Toggle.IsChecked == true);
            bool newState = !allSelected;

            foreach (var row in _storageRows)
                row.Toggle.IsChecked = newState;

            UpdateStorageSelectionSummary();
        }

        private void UpdateStorageSelectionSummary()
        {
            var selected = _storageRows.Where(row => row.Toggle.IsChecked == true).Select(row => row.Category).ToList();
            long bytes = selected.Sum(category => category.SizeBytes);
            bool en = Localization.CurrentLanguage == "en";
            StorageSelectAllButton.Content = selected.Count == _storageRows.Count && selected.Count > 0
                ? Localization.T("Common.DeselectAll")
                : Localization.T("Common.SelectAll");
            StorageDeleteSelectedButton.Content = StorageUiBuilder.DeleteSelectionText(selected.Count, bytes, en);
            StorageDeleteSelectedButton.IsEnabled = selected.Count > 0 && !_isLoadingStorage && !_isDeletingStorage;
        }

        // Wandelt den gespeicherten Zeitpunkt der letzten Bereinigung in eine
        // freundliche, relative Anzeige um (z.B. "vor 3 Tagen", "gerade eben").
        private static string FormatLastCleanup(DateTime? lastCleanupUtc)
        {
            bool en = Localization.CurrentLanguage == "en";

            if (lastCleanupUtc == null) return en ? "never" : "noch nie";

            var diff = DateTime.UtcNow - lastCleanupUtc.Value;

            if (diff.TotalMinutes < 1) return en ? "just now" : "gerade eben";
            if (diff.TotalMinutes < 60) return en ? $"{(int)diff.TotalMinutes} minute(s) ago" : $"vor {(int)diff.TotalMinutes} Minute(n)";
            if (diff.TotalHours < 24) return en ? $"{(int)diff.TotalHours} hour(s) ago" : $"vor {(int)diff.TotalHours} Stunde(n)";
            if (diff.TotalDays < 30) return en ? $"{(int)diff.TotalDays} day(s) ago" : $"vor {(int)diff.TotalDays} Tag(en)";

            return lastCleanupUtc.Value.ToLocalTime().ToString("dd.MM.yyyy");
        }

        private async Task LoadStorage()
        {
            if (_isLoadingStorage || _isDeletingStorage) return;
            _isLoadingStorage = true;
            SetGlobalStatus(Localization.CurrentLanguage == "en" ? "Analyzing storage..." : "Speicher wird analysiert...");
            StoragePanel.Children.Clear();
            StoragePanel.Children.Add(LoadingStateUiBuilder.Create(RootGrid.Resources, 4, !_settings.ReducedMotion));
            _storageRows.Clear();

            StorageRefreshButton.IsEnabled = false;
            StorageDeleteSelectedButton.IsEnabled = false;

            UpdatesLoadingRing.Visibility = Visibility.Visible;
            UpdatesLoadingRing.IsActive = true;

            List<StorageCategory> categories;

            try
            {
                categories = await StorageService.GetCategoriesWithSizesAsync();
            }
            catch (Exception ex)
            {
                Logger.LogError("LoadStorage", ex);
                StoragePanel.Children.Add(new TextBlock
                {
                    Text = $"Fehler beim Ermitteln der Speicherbelegung: {ex.Message}",
                    Foreground = (SolidColorBrush)RootGrid.Resources["AppErrorBrush"]
                });
                return;
            }
            finally
            {
                _isLoadingStorage = false;
                UpdatesLoadingRing.IsActive = false;
                UpdatesLoadingRing.Visibility = Visibility.Collapsed;
                StorageRefreshButton.IsEnabled = true;
                StorageDeleteSelectedButton.IsEnabled = true;
                SetGlobalStatus(null);
            }

            long totalBytes = categories.Sum(c => c.SizeBytes);
            StoragePanel.Children.Clear();
            PageSubtitle.Text = Localization.CurrentLanguage == "en"
                ? $"Last cleaned: {FormatLastCleanup(_settings.LastCleanupUtc)}"
                : $"Zuletzt bereinigt: {FormatLastCleanup(_settings.LastCleanupUtc)}";
            StorageSelectAllButton.Content = Localization.T("Common.SelectAll");

            StoragePanel.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(22),
                Background = (SolidColorBrush)RootGrid.Resources["AppAccentOverlay10"],
                BorderBrush = (SolidColorBrush)RootGrid.Resources["AppAccentOverlay20"],
                BorderThickness = new Thickness(1),
                Child = new StackPanel
                {
                    Spacing = 5,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = Localization.CurrentLanguage == "en" ? "Potential storage gain" : "Möglicher Speichergewinn",
                            Foreground = (SolidColorBrush)RootGrid.Resources["AppMutedForegroundBrush"]
                        },
                        new TextBlock
                        {
                            Text = StorageService.FormatBytes(totalBytes),
                            FontSize = 28,
                            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                            Foreground = (SolidColorBrush)RootGrid.Resources["AppAccentBrushLight"]
                        }
                    }
                }
            });

            var largeFolderResults = new StackPanel { Spacing = 6 };
            IReadOnlyList<LargeFolderResult> analyzedFolders = _cachedLargeFolders;
            CancellationTokenSource? analysisCancellation = null;
            var analysisProgress = new ProgressBar { Height = 4, Visibility = Visibility.Collapsed, IsIndeterminate = true };
            var analysisStatus = new TextBlock
            {
                Foreground = (SolidColorBrush)RootGrid.Resources["AppMutedForegroundBrush"],
                FontSize = 12
            };
            var sortBox = new ComboBox { MinWidth = 150 };
            sortBox.Items.Add(new ComboBoxItem { Content = Localization.CurrentLanguage == "en" ? "Largest first" : "Größte zuerst", Tag = "size" });
            sortBox.Items.Add(new ComboBoxItem { Content = Localization.CurrentLanguage == "en" ? "Name A–Z" : "Name A–Z", Tag = "name" });
            sortBox.Items.Add(new ComboBoxItem { Content = Localization.CurrentLanguage == "en" ? "Risk first" : "Risiko zuerst", Tag = "risk" });
            sortBox.SelectedIndex = 0;
            var thresholdBox = new ComboBox { MinWidth = 150 };
            foreach (var threshold in new[] { ("0", "All folders", "Alle Ordner"), ("104857600", "Over 100 MB", "Über 100 MB"), ("1073741824", "Over 1 GB", "Über 1 GB") })
                thresholdBox.Items.Add(new ComboBoxItem { Content = Localization.CurrentLanguage == "en" ? threshold.Item2 : threshold.Item3, Tag = threshold.Item1 });
            thresholdBox.SelectedIndex = 0;

            void RenderAnalyzedFolders()
            {
                largeFolderResults.Children.Clear();
                long threshold = long.Parse(((ComboBoxItem)thresholdBox.SelectedItem).Tag.ToString()!);
                string sort = ((ComboBoxItem)sortBox.SelectedItem).Tag?.ToString() ?? "size";
                var visibleFolders = analyzedFolders.Where(folder => folder.SizeBytes >= threshold);
                visibleFolders = sort switch
                {
                    "name" => visibleFolders.OrderBy(folder => folder.Path, StringComparer.OrdinalIgnoreCase),
                    "risk" => visibleFolders.OrderByDescending(folder => GetFolderRisk(folder.Path)).ThenByDescending(folder => folder.SizeBytes),
                    _ => visibleFolders.OrderByDescending(folder => folder.SizeBytes)
                };
                foreach (var folder in visibleFolders)
                {
                    var row = new Grid { ColumnSpacing = 8 };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var pathText = new TextBlock { Text = folder.Path, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
                    ToolTipService.SetToolTip(pathText, folder.Path);
                    var sizeText = new TextBlock { Text = StorageService.FormatBytes(folder.SizeBytes), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
                    int risk = GetFolderRisk(folder.Path);
                    var riskText = new TextBlock
                    {
                        Text = risk switch
                        {
                            3 => Localization.CurrentLanguage == "en" ? "System" : "System",
                            2 => Localization.CurrentLanguage == "en" ? "Caution" : "Prüfen",
                            _ => Localization.CurrentLanguage == "en" ? "Normal" : "Normal"
                        },
                        Foreground = risk >= 2
                            ? (SolidColorBrush)RootGrid.Resources["AppWarningBrush"]
                            : (SolidColorBrush)RootGrid.Resources["AppMutedForegroundBrush"],
                        VerticalAlignment = VerticalAlignment.Center,
                        FontSize = 12
                    };
                    Grid.SetColumn(riskText, 1);
                    Grid.SetColumn(sizeText, 2);
                    var openButton = new Button { Content = new FontIcon { Glyph = "\uE838", FontSize = 13 }, Width = 34, Height = 30, Padding = new Thickness(0) };
                    ToolTipService.SetToolTip(openButton, Localization.CurrentLanguage == "en" ? "Open in Explorer" : "Im Explorer öffnen");
                    openButton.Click += (_, __) =>
                    {
                        var result = ExplorerService.OpenFolder(folder.Path);
                        if (result == ExplorerOpenResult.Missing)
                            ShowInfo(Localization.CurrentLanguage == "en" ? "The folder no longer exists." : "Der Ordner ist nicht mehr vorhanden.", InfoBarSeverity.Warning);
                    };
                    Grid.SetColumn(openButton, 3);
                    var copyButton = new Button { Content = new FontIcon { Glyph = "\uE8C8", FontSize = 13 }, Width = 34, Height = 30, Padding = new Thickness(0) };
                    ToolTipService.SetToolTip(copyButton, Localization.CurrentLanguage == "en" ? "Copy folder path" : "Ordnerpfad kopieren");
                    copyButton.Click += (_, __) =>
                    {
                        var data = new Windows.ApplicationModel.DataTransfer.DataPackage();
                        data.SetText(folder.Path);
                        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
                        ShowInfo(Localization.CurrentLanguage == "en" ? "Folder path copied." : "Ordnerpfad kopiert.", InfoBarSeverity.Success);
                    };
                    Grid.SetColumn(copyButton, 4);
                    row.Children.Add(pathText); row.Children.Add(riskText); row.Children.Add(sizeText); row.Children.Add(openButton); row.Children.Add(copyButton);
                    largeFolderResults.Children.Add(row);
                }
            }

            static int GetFolderRisk(string path)
            {
                if (path.Contains("\\Windows", StringComparison.OrdinalIgnoreCase) ||
                    path.Contains("\\Program Files", StringComparison.OrdinalIgnoreCase)) return 3;
                if (path.Contains("\\AppData", StringComparison.OrdinalIgnoreCase) ||
                    path.Contains("\\ProgramData", StringComparison.OrdinalIgnoreCase)) return 2;
                return 1;
            }
            sortBox.SelectionChanged += (_, __) => { if (sortBox.SelectedItem != null && thresholdBox.SelectedItem != null) RenderAnalyzedFolders(); };
            thresholdBox.SelectionChanged += (_, __) => { if (sortBox.SelectedItem != null && thresholdBox.SelectedItem != null) RenderAnalyzedFolders(); };
            var analyzeLargeFoldersButton = new Button
            {
                Content = Localization.CurrentLanguage == "en" ? "Find large folders" : "Große Ordner finden",
                HorizontalAlignment = HorizontalAlignment.Left
            };
            var cancelAnalysisButton = new Button
            {
                Content = Localization.CurrentLanguage == "en" ? "Cancel" : "Abbrechen",
                Visibility = Visibility.Collapsed
            };
            cancelAnalysisButton.Click += (_, __) => analysisCancellation?.Cancel();
            analyzeLargeFoldersButton.Click += async (_, __) =>
            {
                analyzeLargeFoldersButton.IsEnabled = false;
                analyzeLargeFoldersButton.Content = Localization.CurrentLanguage == "en" ? "Analyzing..." : "Wird analysiert...";
                largeFolderResults.Children.Clear();
                analysisCancellation?.Dispose();
                analysisCancellation = new CancellationTokenSource();
                analysisProgress.Visibility = Visibility.Visible;
                cancelAnalysisButton.Visibility = Visibility.Visible;
                try
                {
                    var watch = Stopwatch.StartNew();
                    var progress = new Progress<int>(count =>
                        analysisStatus.Text = Localization.CurrentLanguage == "en"
                            ? count + " folders checked"
                            : count + " Ordner geprüft");
                    analyzedFolders = await LargeFolderAnalyzer.AnalyzeAsync(analysisCancellation.Token, progress);
                    _cachedLargeFolders = analyzedFolders;
                    _largeFolderAnalysisUtc = DateTime.UtcNow;
                    watch.Stop();
                    analysisStatus.Text = Localization.CurrentLanguage == "en"
                        ? analyzedFolders.Count + " results · " + watch.Elapsed.TotalSeconds.ToString("0.0") + " seconds"
                        : analyzedFolders.Count + " Ergebnisse · " + watch.Elapsed.TotalSeconds.ToString("0.0") + " Sekunden";
                    RenderAnalyzedFolders();
                }
                catch (OperationCanceledException)
                {
                    largeFolderResults.Children.Add(new TextBlock { Text = Localization.CurrentLanguage == "en" ? "Analysis cancelled." : "Analyse abgebrochen." });
                }
                catch (Exception ex)
                {
                    Logger.LogError("Große Ordner analysieren", ex);
                    largeFolderResults.Children.Add(new TextBlock
                    {
                        Text = Localization.CurrentLanguage == "en" ? "The analysis could not be completed." : "Die Analyse konnte nicht abgeschlossen werden."
                    });
                }
                finally
                {
                    analyzeLargeFoldersButton.IsEnabled = true;
                    analyzeLargeFoldersButton.Content = Localization.CurrentLanguage == "en" ? "Analyze again" : "Erneut analysieren";
                    analysisProgress.Visibility = Visibility.Collapsed;
                    cancelAnalysisButton.Visibility = Visibility.Collapsed;
                }
            };
            var analyzerContent = new StackPanel { Spacing = 10 };
            analyzerContent.Children.Add(new TextBlock
            {
                Text = Localization.CurrentLanguage == "en"
                    ? "Scans your personal folders on demand. Nothing is deleted."
                    : "Durchsucht persönliche Ordner nur auf Knopfdruck. Es wird nichts gelöscht.",
                Foreground = (SolidColorBrush)RootGrid.Resources["AppMutedForegroundBrush"]
            });
            var riskLegend = new TextBlock
            {
                Text = Localization.CurrentLanguage == "en"
                    ? "Risk: Normal = personal folder · Caution = application data · System = Windows or program files"
                    : "Risiko: Normal = persönlicher Ordner · Prüfen = Anwendungsdaten · System = Windows- oder Programmdateien",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (SolidColorBrush)RootGrid.Resources["AppMutedForegroundBrush"]
            };
            ToolTipService.SetToolTip(riskLegend, Localization.CurrentLanguage == "en"
                ? "Risk is informational only. WinVora does not delete these folders automatically."
                : "Die Risikoeinstufung ist nur ein Hinweis. WinVora löscht diese Ordner niemals automatisch.");
            analyzerContent.Children.Add(riskLegend);
            var analyzerActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            analyzerActions.Children.Add(analyzeLargeFoldersButton);
            analyzerActions.Children.Add(cancelAnalysisButton);
            analyzerActions.Children.Add(sortBox);
            analyzerActions.Children.Add(thresholdBox);
            analyzerContent.Children.Add(analyzerActions);
            analyzerContent.Children.Add(analysisProgress);
            analyzerContent.Children.Add(analysisStatus);
            analyzerContent.Children.Add(largeFolderResults);
            if (analyzedFolders.Count > 0)
            {
                analysisStatus.Text = Localization.CurrentLanguage == "en"
                    ? "Cached result from " + _largeFolderAnalysisUtc?.ToLocalTime().ToString("g")
                    : "Zwischengespeichert vom " + _largeFolderAnalysisUtc?.ToLocalTime().ToString("g");
                RenderAnalyzedFolders();
            }
            StoragePanel.Children.Add(new Expander
            {
                Header = Localization.CurrentLanguage == "en" ? "Storage analysis" : "Speicheranalyse",
                Content = analyzerContent,
                Padding = new Thickness(4),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            });

            var byKey = categories.ToDictionary(c => c.Key);

            // Gruppiert die Kategorien thematisch, damit nicht 20 einzelne
            // Karten untereinander stehen, sondern ausklappbare Abschnitte
            // (gleiches Prinzip wie bei den Systeminfo-Kategorien).
            var groups = new (string Title, string[] Keys, bool Expanded)[]
            {
                (Localization.T("Storage.TempFiles"), new[] { "user_temp", "windows_temp", "prefetch", "inet_cache" }, false),
                (Localization.T("Storage.RecycleDownloads"), new[] { "recycle_bin", "update_cache", "delivery_optimization", "upgrade_logs", "old_install_files" }, false),
                (Localization.T("Storage.SystemCaches"), new[] { "dx_shader_cache", "thumbnail_cache", "store_cache", "dns_cache" }, false),
                (Localization.T("Storage.ErrorLogs"), new[] { "wer", "minidump", "crash_dumps", "logs", "setup_logs", "defender_temp" }, false),
                (Localization.T("Storage.Browser"), new[] { "browser_cache" }, false),
            };

            foreach (var group in groups)
            {
                var groupCategories = group.Keys.Where(byKey.ContainsKey).Select(k => byKey[k]).ToList();
                if (groupCategories.Count == 0) continue;

                long groupBytes = groupCategories.Sum(c => c.SizeBytes);

                var expander = new Expander
                {
                    Header = $"{group.Title}  •  {StorageService.FormatBytes(groupBytes)}",
                    IsExpanded = group.Expanded,
                    FontSize = 18,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    MinHeight = 56,
                    Padding = new Thickness(4),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Margin = new Thickness(0, 0, 0, 4)
                };
                expander.RegisterPropertyChangedCallback(Expander.IsExpandedProperty, (_, __) =>
                    expander.Background = expander.IsExpanded
                        ? (SolidColorBrush)RootGrid.Resources["AppAccentOverlay10"]
                        : new SolidColorBrush(Microsoft.UI.Colors.Transparent));

                var groupPanel = new StackPanel { Spacing = 4, Margin = new Thickness(4) };

                foreach (var category in groupCategories)
                {
                    groupPanel.Children.Add(MakeStorageCard(category));
                }

                expander.Content = groupPanel;
                StoragePanel.Children.Add(expander);
            }
            UpdateStorageSelectionSummary();
        }
    }
}
