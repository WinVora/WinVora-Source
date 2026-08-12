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

        private ToolkitControls.SettingsCard MakeStorageCard(StorageCategory category)
        {
            var toggle = new ToggleSwitch { IsOn = false, OnContent = "", OffContent = "" };

            var deleteButton = new Button { Content = "Löschen" };
            var deleteNormalBackground = deleteButton.Background;
            deleteButton.PointerEntered += (_, __) =>
                deleteButton.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x35, 0xFF, 0x6B, 0x6B));
            deleteButton.PointerExited += (_, __) => deleteButton.Background = deleteNormalBackground;
            deleteButton.Click += async (_, __) => await DeleteSingleCategory(category, deleteButton);

            var actionsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                VerticalAlignment = VerticalAlignment.Center
            };
            bool advanced = category.RequiresAdmin || category.Key is "prefetch" or "old_install_files" or "minidump" or "crash_dumps";
            var cautionBadge = new Border
            {
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(8, 4, 8, 4),
                Background = new SolidColorBrush(advanced
                    ? Windows.UI.Color.FromArgb(0x24, 0xFF, 0xC1, 0x4D)
                    : Windows.UI.Color.FromArgb(0x24, 0x4C, 0xD9, 0x73)),
                Child = new TextBlock
                {
                    Text = advanced
                        ? (Localization.CurrentLanguage == "en" ? "Use caution" : "Mit Vorsicht")
                        : (Localization.CurrentLanguage == "en" ? "Safe cleanup" : "Unbedenklich"),
                    FontSize = 11,
                    Foreground = new SolidColorBrush(advanced
                        ? Windows.UI.Color.FromArgb(0xFF, 0xA8, 0x68, 0x00)
                        : Windows.UI.Color.FromArgb(0xFF, 0x18, 0x78, 0x3C))
                }
            };
            ToolTipService.SetToolTip(cautionBadge, advanced
                ? (Localization.CurrentLanguage == "en"
                    ? "Optional cleanup of Windows, diagnostic, or startup data. Review it before deleting."
                    : "Optionale Bereinigung von Windows-, Diagnose- oder Startdaten. Vor dem Löschen kurz prüfen.")
                : (Localization.CurrentLanguage == "en"
                    ? "Normally removes only temporary or automatically recreated files."
                    : "Entfernt normalerweise nur temporäre oder automatisch neu erstellte Dateien."));
            actionsPanel.Children.Add(cautionBadge);
            actionsPanel.Children.Add(toggle);
            actionsPanel.Children.Add(deleteButton);

            var descriptionSuffix = category.RequiresAdmin
                ? (Localization.CurrentLanguage == "en" ? "  •  Administrator rights required" : "  •  Administratorrechte erforderlich")
                : advanced
                    ? (Localization.CurrentLanguage == "en" ? "  •  Review before deleting" : "  •  Vor dem Löschen prüfen")
                    : "";

            var card = new ToolkitControls.SettingsCard
            {
                Header = category.Name,
                Description = $"{category.Description}{descriptionSuffix}  •  {category.SizeDisplay}",
                HeaderIcon = new FontIcon { Glyph = GetStorageIconGlyph(category.Key) },
                Content = actionsPanel,
                BorderThickness = new Thickness(1),
                BorderBrush = (SolidColorBrush)RootGrid.Resources["AppOverlay28"]
            };

            // Akzentfarbener Rand, solange die Kategorie zum Löschen ausgewählt ist.
            var defaultBorder = card.BorderBrush;
            var accentBorder = (SolidColorBrush)RootGrid.Resources["AppAccentBrushLight"];
            toggle.Toggled += (_, __) => card.BorderBrush = toggle.IsOn ? accentBorder : defaultBorder;
            toggle.Toggled += (_, __) => UpdateStorageSelectionSummary();

            _storageRows.Add((category, toggle));
            return card;
        }

        // Ordnet jeder Storage-Kategorie ein passendes Fluent-Icon-Glyph zu.
        private static string GetStorageIconGlyph(string categoryKey) => categoryKey switch
        {
            "user_temp" or "windows_temp" => "\uE74D",       // Papierkorb-artiges Symbol für Temp
            "downloads" => "\uE896",
            "prefetch" => "\uE945",                          // Blitz / Performance
            "recycle_bin" => "\uE74D",                        // Papierkorb
            "dx_shader_cache" => "\uE7F4",                    // Grafikkarte
            "update_cache" or "delivery_optimization" => "\uE895", // Download/Update
            "wer" or "minidump" or "crash_dumps" => "\uE783", // Warnung
            "thumbnail_cache" => "\uEB9F",                    // Bilder
            "browser_cache" or "inet_cache" => "\uE774",      // Globus/Web
            "logs" or "setup_logs" or "upgrade_logs" => "\uE7C3", // Dokument
            "defender_temp" => "\uEA18",                      // Schild
            "store_cache" => "\uE719",                        // Store-Symbol
            "dns_cache" => "\uE968",                          // Netzwerk
            "old_install_files" => "\uE7B8",                  // Paket/App
            _ => "\uE8B7"                                     // Standard: Ordner
        };

        // Prüft, ob eine Kategorie betroffene Browser-Prozesse hat, die gerade
        // laufen - dann schlagen einzelne Dateien beim Löschen fehl, weil sie
        // in Benutzung sind. Liefert einen Warnhinweis oder "" falls nichts zu melden ist.
        private static string GetRunningProcessWarning(IEnumerable<StorageCategory> categories)
        {
            if (!categories.Any(c => c.Key == "browser_cache")) return "";

            var runningBrowsers = new List<string>();
            if (Process.GetProcessesByName("chrome").Length > 0) runningBrowsers.Add("Chrome");
            if (Process.GetProcessesByName("msedge").Length > 0) runningBrowsers.Add("Edge");

            if (runningBrowsers.Count == 0) return "";

            return $"\n\nHinweis: {string.Join(" und ", runningBrowsers)} läuft gerade - " +
                   "einige Cache-Dateien sind dadurch in Benutzung und werden übersprungen. " +
                   "Für eine vollständige Bereinigung den Browser vorher schließen.";
        }

        private bool RequiresProtectedCleanupConfirmation(IEnumerable<StorageCategory> categories)
        {
            var keys = categories.Select(category => category.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return (_settings.ConfirmDownloadsCleanup && keys.Contains("downloads")) ||
                   (_settings.ConfirmRecycleBinCleanup && keys.Contains("recycle_bin")) ||
                   (_settings.ConfirmBrowserCleanup && (keys.Contains("browser_cache") || keys.Contains("inet_cache")));
        }

        private string GetProtectedCleanupWarning(IEnumerable<StorageCategory> categories)
        {
            var keys = categories.Select(category => category.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var warnings = new List<string>();
            if (_settings.ConfirmDownloadsCleanup && keys.Contains("downloads"))
                warnings.Add("Downloads können persönliche Dateien enthalten");
            if (_settings.ConfirmRecycleBinCleanup && keys.Contains("recycle_bin"))
                warnings.Add("Dateien im Papierkorb werden endgültig gelöscht");
            if (_settings.ConfirmBrowserCleanup && (keys.Contains("browser_cache") || keys.Contains("inet_cache")))
                warnings.Add("Browserdaten können Anmeldungen oder Offline-Inhalte beeinflussen");
            return warnings.Count == 0 ? "" : "\n\nBesonders geschützt: " + string.Join("; ", warnings) + ".";
        }

        // Löscht eine oder mehrere Storage-Kategorien. Kategorien, die
        // Admin-Rechte brauchen, laufen über einen kurzen elevierten
        // Hilfsprozess (ein UAC-Prompt für alle zusammen); alles andere läuft
        // direkt im normalen, nicht elevierten Prozess.
        private async Task<(bool success, string message)> DeleteCategoriesAsync(List<StorageCategory> categories)
        {
            var adminCategories = categories.Where(c => c.RequiresAdmin).ToList();
            var normalCategories = categories.Where(c => !c.RequiresAdmin).ToList();

            var messages = new List<string>();
            bool overallSuccess = true;

            foreach (var category in normalCategories)
            {
                var (success, message) = await StorageService.DeleteCategoryAsync(category);
                messages.Add($"{category.Name}: {message}");
                if (!success) overallSuccess = false;
            }

            if (adminCategories.Count > 0)
            {
                var exitCode = await RunElevatedStorageDeleteAsync(adminCategories);

                if (exitCode == 0)
                {
                    messages.Add(adminCategories.Count == 1
                        ? $"{adminCategories[0].Name}: erfolgreich gelöscht (mit Admin-Rechten)."
                        : $"{adminCategories.Count} Admin-Bereiche erfolgreich gelöscht.");
                }
                else if (exitCode == 1223) // ERROR_CANCELLED - Nutzer hat UAC abgelehnt
                {
                    overallSuccess = false;
                    messages.Add("Admin-Rechte wurden nicht erteilt - Admin-pflichtige Bereiche wurden übersprungen.");
                }
                else
                {
                    overallSuccess = false;
                    messages.Add("Einige Admin-pflichtige Bereiche konnten nicht (vollständig) gelöscht werden.");
                }
            }

            return (overallSuccess, string.Join("  •  ", messages));
        }

        // Startet den elevierten Hilfsprozess für Admin-pflichtige Löschungen
        // und liefert dessen Exitcode zurück (0 = alles erfolgreich).
        private async Task<int> RunElevatedStorageDeleteAsync(List<StorageCategory> categories)
        {
            try
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath))
                    throw new InvalidOperationException("Eigener Programmpfad konnte nicht ermittelt werden.");

                var keyList = string.Join(";", categories.Select(c => c.Key));

                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = $"--delete-storage \"{keyList}\"",
                    UseShellExecute = true,
                    Verb = "runas" // löst den UAC-Prompt nur für diesen einen Vorgang aus
                };

                using var proc = Process.Start(psi);
                if (proc != null) await proc.WaitForExitAsync();

                return proc?.ExitCode ?? -1;
            }
            catch (System.ComponentModel.Win32Exception winEx) when (winEx.NativeErrorCode == 1223)
            {
                // Nutzer hat den UAC-Prompt mit "Nein" abgebrochen
                Logger.Log("Elevierte Storage-Löschung vom Nutzer abgebrochen (UAC verweigert).");
                return 1223;
            }
            catch (Exception ex)
            {
                Logger.LogError("RunElevatedStorageDeleteAsync", ex);
                return -1;
            }
        }

        private async Task DeleteSingleCategory(StorageCategory category, Button sourceButton)
        {
            if (_isDeletingStorage || _isLoadingStorage) return;

            bool confirmed = await ConfirmAsync(
                "Bereich löschen?",
                $"\"{category.Name}\" wird bereinigt. Das kann nicht rückgängig gemacht werden. Fortfahren?" +
                GetProtectedCleanupWarning(new[] { category }) +
                GetRunningProcessWarning(new[] { category }),
                respectDeleteConfirmationSetting: !RequiresProtectedCleanupConfirmation(new[] { category }));

            if (!confirmed) return;

            sourceButton.IsEnabled = false;
            StorageRefreshButton.IsEnabled = false;
            StorageDeleteSelectedButton.IsEnabled = false;

            StorageProgressPanel.Visibility = Visibility.Visible;
            StorageProgressBar.Maximum = 1;
            StorageProgressBar.Value = 0;
            StorageProgressText.Text = category.RequiresAdmin
                ? $"Lösche {category.Name}... (Admin-Bestätigung nötig)"
                : $"Lösche {category.Name}...";

            var (success, message) = await DeleteCategoriesAsync(new List<StorageCategory> { category });
            Logger.Log($"Storage-Löschung '{category.Name}': {(success ? "OK" : "Fehler")} - {message}");

            if (success)
            {
                _settings.LastCleanupUtc = DateTime.UtcNow;
                _settings.Save();
                LogActivity("\uE74D",
                    $"{category.Name} bereinigt ({category.SizeDisplay})",
                    $"Cleaned {category.Name} ({category.SizeDisplay})");
            }
            else
            {
                LogActivity("\uEA39",
                    $"Bereinigung von {category.Name} fehlgeschlagen",
                    $"Failed to clean {category.Name}",
                    "Failed");
            }

            StorageProgressBar.Value = 1;
            StorageProgressText.Text = success
                ? $"{category.Name}: {message}"
                : $"{category.Name} - Fehler: {message}";

            await Task.Delay(1500);
            StorageProgressPanel.Visibility = Visibility.Collapsed;

            _isDeletingStorage = false;
            await LoadStorage();
        }

        private async void StorageDeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            if (_isDeletingStorage || _isLoadingStorage) return;
            var selected = _storageRows.Where(r => r.Toggle.IsOn).Select(r => r.Category).ToList();

            if (selected.Count == 0)
            {
                StorageProgressPanel.Visibility = Visibility.Visible;
                StorageProgressText.Text = "Keine Bereiche ausgewählt.";
                StorageProgressBar.Value = 0;
                return;
            }

            bool confirmed = await ConfirmAsync(
                "Ausgewählte Bereiche löschen?",
                $"{selected.Count} Bereich(e) werden bereinigt: {string.Join(", ", selected.Select(c => c.Name))}. Das kann nicht rückgängig gemacht werden. Fortfahren?" +
                GetProtectedCleanupWarning(selected) +
                GetRunningProcessWarning(selected),
                respectDeleteConfirmationSetting: !RequiresProtectedCleanupConfirmation(selected));

            if (!confirmed) return;
            _isDeletingStorage = true;

            StorageRefreshButton.IsEnabled = false;
            StorageDeleteSelectedButton.IsEnabled = false;

            StorageProgressPanel.Visibility = Visibility.Visible;

            var normalCategories = selected.Where(c => !c.RequiresAdmin).ToList();
            var adminCategories = selected.Where(c => c.RequiresAdmin).ToList();

            StorageProgressBar.Maximum = normalCategories.Count + (adminCategories.Count > 0 ? 1 : 0);
            StorageProgressBar.Value = 0;

            var results = new List<string>();
            bool anySuccess = false;
            int step = 0;

            foreach (var category in normalCategories)
            {
                step++;
                StorageProgressText.Text = $"Lösche {category.Name} ({step}/{StorageProgressBar.Maximum})...";

                var (success, message) = await StorageService.DeleteCategoryAsync(category);
                results.Add(success ? $"{category.Name}: OK" : $"{category.Name}: Fehler");
                Logger.Log($"Storage-Sammel-Löschung '{category.Name}': {(success ? "OK" : "Fehler")} - {message}");
                if (success) anySuccess = true;

                StorageProgressBar.Value = step;
            }

            if (adminCategories.Count > 0)
            {
                step++;
                StorageProgressText.Text = $"Lösche {adminCategories.Count} Admin-Bereich(e)... (Admin-Bestätigung nötig)";

                var exitCode = await RunElevatedStorageDeleteAsync(adminCategories);
                bool adminSuccess = exitCode == 0;

                foreach (var category in adminCategories)
                {
                    results.Add(adminSuccess ? $"{category.Name}: OK" : $"{category.Name}: Fehler");
                    Logger.Log($"Storage-Sammel-Löschung (elevated) '{category.Name}': {(adminSuccess ? "OK" : $"Fehler (ExitCode {exitCode})")}");
                }

                if (adminSuccess) anySuccess = true;
                StorageProgressBar.Value = step;
            }

            StorageProgressText.Text = "Bereinigung abgeschlossen: " + string.Join(", ", results);

            bool anyFailure = results.Any(result => result.Contains("Fehler", StringComparison.OrdinalIgnoreCase));
            if (anySuccess)
            {
                _settings.LastCleanupUtc = DateTime.UtcNow;
                _settings.Save();

                long totalFreedBytes = selected.Sum(c => c.SizeBytes);
                var freedDisplay = StorageService.FormatBytes(totalFreedBytes);
                LogActivity("\uE74D",
                    $"{selected.Count} Bereich(e) bereinigt ({freedDisplay})",
                    $"Cleaned {selected.Count} area(s) ({freedDisplay})",
                    anyFailure ? "Failed" : "Successful");
            }
            else
            {
                LogActivity("\uEA39",
                    "Bereinigung fehlgeschlagen",
                    "Cleanup failed",
                    "Failed");
            }

            await Task.Delay(2500);
            StorageProgressPanel.Visibility = Visibility.Collapsed;

            _isDeletingStorage = false;
            await LoadStorage();
        }
    }
}
