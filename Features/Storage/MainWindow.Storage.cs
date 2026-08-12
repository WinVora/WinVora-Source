using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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

            bool allSelected = _storageRows.All(r => r.Toggle.IsOn);
            bool newState = !allSelected;

            foreach (var row in _storageRows)
                row.Toggle.IsOn = newState;

            UpdateStorageSelectionSummary();
        }

        private void UpdateStorageSelectionSummary()
        {
            var selected = _storageRows.Where(row => row.Toggle.IsOn).Select(row => row.Category).ToList();
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
            StoragePanel.Children.Add(new TextBlock
            {
                Text = Localization.CurrentLanguage == "en" ? "Calculating reclaimable storage..." : "Möglicher Speichergewinn wird berechnet...",
                Foreground = (SolidColorBrush)RootGrid.Resources["AppMutedForegroundBrush"],
                Margin = new Thickness(4, 16, 0, 8)
            });
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
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.OrangeRed)
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
