using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ToolkitControls = CommunityToolkit.WinUI.Controls;

namespace WinVora
{
    public sealed partial class MainWindow
    {

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
            var cautionBadge = CommonUiBuilder.CreateStatusBadge(
                advanced
                    ? (Localization.CurrentLanguage == "en" ? "Use caution" : "Mit Vorsicht")
                    : (Localization.CurrentLanguage == "en" ? "Safe cleanup" : "Unbedenklich"),
                advanced,
                RootGrid.Resources);
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
    }
}
