using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace WinVora
{
    public sealed partial class MainWindow
    {
        private async Task LoadPcChangesAsync()
        {
            try
            {
                var result = await StartupPerformanceTracker.MeasureAsync(
                    "PC-Veränderungen",
                    () => PcChangesService.CaptureAndCompareAsync(_installedPrograms, _startupCancellation.Token));
                _pcChangeSummary = result.Summary;
                RenderPcChanges();
                if (result.Summary.StorageGrowth.Count > 0)
                {
                    ShowInfo(Localization.CurrentLanguage == "en"
                        ? $"Storage hog detected: {result.Summary.StorageGrowth[0].Name} grew by {StorageService.FormatBytes(result.Summary.StorageGrowth[0].GrowthBytes)}."
                        : $"Speicherfresser erkannt: {result.Summary.StorageGrowth[0].Name} ist um {StorageService.FormatBytes(result.Summary.StorageGrowth[0].GrowthBytes)} gewachsen.",
                        InfoBarSeverity.Warning);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Logger.LogError("PC-Veränderungen laden", ex);
                DashChangesText.Text = Localization.CurrentLanguage == "en"
                    ? "Changes could not be checked"
                    : "Veränderungen konnten nicht geprüft werden";
            }
        }

        private void RenderPcChanges()
        {
            bool en = Localization.CurrentLanguage == "en";
            ChangesSummaryPanel.Children.Clear();
            StorageGrowthPanel.Children.Clear();
            var summary = _pcChangeSummary;
            if (summary == null || !summary.HasBaseline)
            {
                DashChangesText.Text = en ? "Baseline saved for the next comparison" : "Vergleichspunkt für den nächsten Start gespeichert";
                ChangesComparedAtText.Text = en ? "The next start will show changes." : "Beim nächsten Start zeigt WinVora die Veränderungen an.";
                ChangesStatusBadge.Text = en ? "Baseline created" : "Vergleichspunkt erstellt";
                StorageGrowthBadge.Text = en ? "From next check" : "Ab nächster Prüfung";
                var baselineState = MakeEmptyState("\uE9D2", en ? "Baseline created" : "Vergleichspunkt erstellt",
                    en ? "No earlier state was available." : "Es war noch kein früherer Stand vorhanden.");
                Grid.SetColumnSpan(baselineState, 3);
                ChangesSummaryPanel.Children.Add(baselineState);
                StorageGrowthPanel.Children.Add(MakeEmptyState("\uEDA2", en ? "No comparison yet" : "Noch kein Vergleich",
                    en ? "Storage growth will be visible after the next check." : "Speicherwachstum wird nach der nächsten Prüfung sichtbar."));
                return;
            }

            DateTime previousLocal = summary.PreviousUtc.GetValueOrDefault().ToLocalTime();
            ChangesComparedAtText.Text = en
                ? $"Compared with {previousLocal:g}"
                : $"Verglichen mit {previousLocal:g}";
            var parts = new List<string>();
            if (summary.InstalledPrograms > 0) parts.Add(en ? $"{summary.InstalledPrograms} installed" : $"{summary.InstalledPrograms} installiert");
            if (summary.RemovedPrograms > 0) parts.Add(en ? $"{summary.RemovedPrograms} removed" : $"{summary.RemovedPrograms} entfernt");
            if (summary.UpdatedPrograms > 0) parts.Add(en ? $"{summary.UpdatedPrograms} updated" : $"{summary.UpdatedPrograms} aktualisiert");
            if (summary.AddedStartupEntries > 0) parts.Add(en ? $"{summary.AddedStartupEntries} new startup items" : $"{summary.AddedStartupEntries} neue Autostarts");
            if (summary.FreeSpaceDifferenceBytes < -100 * 1024 * 1024) parts.Add(en
                ? $"{StorageService.FormatBytes(-summary.FreeSpaceDifferenceBytes)} less free"
                : $"{StorageService.FormatBytes(-summary.FreeSpaceDifferenceBytes)} weniger frei");
            DashChangesText.Text = parts.Count == 0 ? (en ? "No notable changes" : "Keine auffälligen Veränderungen") : string.Join(" · ", parts);
            int totalChanges = summary.InstalledPrograms + summary.RemovedPrograms + summary.UpdatedPrograms +
                               summary.AddedStartupEntries + summary.RemovedStartupEntries;
            ChangesStatusBadge.Text = totalChanges == 0
                ? (en ? "No changes" : "Keine Änderungen")
                : (en ? $"{totalChanges} changes" : $"{totalChanges} Änderungen");

            AddChangeMetric(en ? "Installed" : "Installiert", summary.InstalledPrograms, "\uE710", 0, 0, "#4CD973");
            AddChangeMetric(en ? "Removed" : "Entfernt", summary.RemovedPrograms, "\uE74D", 0, 1, "#FF6B6B");
            AddChangeMetric(en ? "Updated" : "Aktualisiert", summary.UpdatedPrograms, "\uE895", 0, 2, "#8B7CF6");
            AddChangeMetric(en ? "New startup items" : "Neue Autostarts", summary.AddedStartupEntries, "\uE768", 1, 0, "#F5B942");
            AddChangeMetric(en ? "Removed startup items" : "Entfernte Autostarts", summary.RemovedStartupEntries, "\uE711", 1, 1, "#9AA0AA");
            string storageDifference = summary.FreeSpaceDifferenceBytes switch
            {
                < 0 => $"−{StorageService.FormatBytes(-summary.FreeSpaceDifferenceBytes)}",
                > 0 => $"+{StorageService.FormatBytes(summary.FreeSpaceDifferenceBytes)}",
                _ => "0 B"
            };
            AddChangeMetric(en ? "Free space" : "Freier Speicher", storageDifference, "\uEDA2", 1, 2,
                summary.FreeSpaceDifferenceBytes < 0 ? "#F5B942" : "#4CD973");

            if (summary.StorageGrowth.Count == 0)
            {
                StorageGrowthBadge.Text = en ? "No alert" : "Keine Warnung";
                StorageGrowthPanel.Children.Add(MakeEmptyState("\uEDA2", en ? "No storage hog detected" : "Kein Speicherfresser erkannt",
                    en ? "No watched folder grew by more than 1 GB." : "Keiner der überwachten Ordner ist um mehr als 1 GB gewachsen."));
            }
            else
            {
                StorageGrowthBadge.Text = en ? $"{summary.StorageGrowth.Count} alerts" : $"{summary.StorageGrowth.Count} Warnungen";
                foreach (var growth in summary.StorageGrowth)
                {
                    StorageGrowthPanel.Children.Add(PcChangesUiBuilder.CreateStorageWarning(
                        growth, en,
                        (Brush)RootGrid.Resources["AppOverlay10"],
                        (Brush)RootGrid.Resources["AppMutedForegroundBrush"],
                        path =>
                    {
                        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true }); }
                        catch (Exception ex) { Logger.LogError("Speicherfresser-Ordner öffnen", ex); }
                    }));
                }
            }
        }

        private void AddChangeMetric(string title, int count, string glyph, int row, int column, string color) =>
            AddChangeMetric(title, count.ToString(), glyph, row, column, color);

        private void AddChangeMetric(string title, string value, string glyph, int row, int column, string color)
        {
            while (ChangesSummaryPanel.RowDefinitions.Count <= row)
                ChangesSummaryPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var card = PcChangesUiBuilder.CreateMetric(title, value, glyph, color,
                (Brush)RootGrid.Resources["AppForegroundBrush"],
                (Brush)RootGrid.Resources["AppMutedForegroundBrush"],
                (Brush)RootGrid.Resources["AppOverlay10"],
                (Brush)RootGrid.Resources["AppOverlay22"]);
            Grid.SetRow(card, row); Grid.SetColumn(card, column);
            ChangesSummaryPanel.Children.Add(card);
        }

        private void Changes_Click(object sender, RoutedEventArgs e)
        {
            SetPage("Changes");
            RenderPcChanges();
        }

        private void DashChanges_Tapped(object sender, TappedRoutedEventArgs e) => Changes_Click(sender, e);
    }
}
