using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ToolkitControls = CommunityToolkit.WinUI.Controls;

namespace WinVora
{
    public sealed partial class MainWindow
    {
        private async Task LoadWingetIconsInBackground(
            List<(WingetPackage Package, CheckBox Toggle, ToolkitControls.SettingsCard Card, string BaseDescription)> rows)
        {
            try
            {
                var installedPrograms = await Task.Run(() => InstalledProgramsService.GetInstalledPrograms(resolveIcons: false));

                foreach (var row in rows)
                {
                    if (!_wingetRows.Any(current => ReferenceEquals(current.Card, row.Card))) return;
                    var program = InstalledProgramsService.FindProgramForName(installedPrograms, row.Package.Name);
                    if (program != null) _wingetIconCards[row.Card] = program;
                }
                LoadVisibleWingetIcons();
            }
            catch (Exception ex)
            {
                Logger.LogError("LoadWingetIconsInBackground", ex);
            }
        }

        private void LoadVisibleWingetIcons()
        {
            double viewportTop = MainContentScrollViewer.VerticalOffset;
            double viewportBottom = viewportTop + MainContentScrollViewer.ViewportHeight + 240;
            foreach (var pair in _wingetIconCards)
            {
                if (pair.Key.Visibility != Visibility.Visible || _loadedWingetIcons.Contains(pair.Key)) continue;
                try
                {
                    double y = pair.Key.TransformToVisual(ContentArea).TransformPoint(new Windows.Foundation.Point()).Y;
                    if (y + pair.Key.ActualHeight < viewportTop || y > viewportBottom) continue;
                    _loadedWingetIcons.Add(pair.Key);
                    _ = ResolveAndLoadCardIconAsync(pair.Key, pair.Value);
                }
                catch (Exception ex)
                {
                    Logger.LogErrorOnce("Sichtbares Update-Symbol bestimmen", ex);
                }
            }
        }

        // BUGFIX (Lag-Problem): Vorher liefen bis zu 4 "winget show"-Prozesse
        // gleichzeitig UND jedes einzelne Ergebnis hat sofort für sich ein
        // UI-Update (Card.Description) samt Relayout ausgelöst. Bei vielen
        // Updates kamen so kurz hintereinander viele einzelne Relayouts der
        // gesamten Liste zusammen - das war der spürbare Ruckler beim Öffnen
        // von Winget. Jetzt: weniger parallele Prozesse UND alle fertigen
        // Ergebnisse werden gesammelt und nur alle 300ms in einem Rutsch
        // angewendet, statt sofort bei jedem einzelnen Treffer.
        private async Task LoadWingetDetailsInBackground(
            List<(WingetPackage Package, CheckBox Toggle, ToolkitControls.SettingsCard Card, string BaseDescription)> rows)
        {
            using var semaphore = new SemaphoreSlim(2);
            var installedPrograms = await Task.Run(() => InstalledProgramsService.GetInstalledPrograms(resolveIcons: false));
            var publisherLabel = Localization.T("Winget.Publisher");
            var sizeLabel = Localization.T("Winget.Size");

            var pending = new System.Collections.Concurrent.ConcurrentQueue<(ToolkitControls.SettingsCard Card, string Text, string Details)>();

            void FlushPending()
            {
                while (pending.TryDequeue(out var item))
                {
                    item.Card.Description = item.Text;
                    if (item.Card.Tag is TextBlock detailsText)
                        detailsText.Text = item.Details;
                }
            }

            var flushTimer = DispatcherQueue.CreateTimer();
            flushTimer.Interval = TimeSpan.FromMilliseconds(300);
            flushTimer.IsRepeating = true;
            flushTimer.Tick += (_, __) => FlushPending();
            flushTimer.Start();

            try
            {
                var tasks = rows.Select(async row =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        var (publisher, size) = await GetWingetDetailsAsync(
                            row.Package.Id, row.Package.Name, installedPrograms);
                        row.Package.Publisher = publisher;
                        row.Package.DownloadSize = size;
                        pending.Enqueue((row.Card,
                            $"{row.BaseDescription}     {sizeLabel.ToUpperInvariant()}  {size}     {publisherLabel.ToUpperInvariant()}  {publisher}",
                            UpdateUiBuilder.TechnicalDetails(row.Package, Localization.CurrentLanguage == "en")));
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                await Task.WhenAll(tasks);
            }
            finally
            {
                flushTimer.Stop();
                FlushPending(); // letzte übrig gebliebene Ergebnisse noch anwenden
            }
        }

        // Liest "winget show --id X" aus und sucht sprachunabhängig nach
        // Herausgeber- und Größenangaben. Das genaue Textformat kann je nach
        // winget-Version/Sprache leicht variieren.
        private async Task<(string Publisher, string Size)> GetWingetDetailsAsync(
            string packageId, string packageName, List<InstalledProgram> installedPrograms)
        {
            string publisher = "N/A";
            string size = "N/A";
            using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(_startupCancellation.Token);
            timeoutCancellation.CancelAfter(TimeSpan.FromSeconds(30));
            CancellationToken token = timeoutCancellation.Token;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "winget",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                psi.ArgumentList.Add("show");
                psi.ArgumentList.Add("--id");
                psi.ArgumentList.Add(packageId);
                psi.ArgumentList.Add("--accept-source-agreements");
                // Hinweis: "--disable-interactivity" bewusst NICHT gesetzt - ältere
                // winget-Versionen kennen dieses Flag nicht und brechen dann den
                // kompletten Befehl mit einem Fehler ab, was zu durchgängigem
                // "N/A" bei Herausgeber/Größe führt (auch wenn winget selbst
                // grundsätzlich funktioniert).

                using var p = new Process { StartInfo = psi };
                p.Start();
                using var cancellationRegistration = token.Register(() =>
                {
                    try
                    {
                        if (!p.HasExited) p.Kill(entireProcessTree: true);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogErrorOnce($"winget show '{packageId}' abbrechen", ex);
                    }
                });

                var foundDownloadSize = false;

                async Task ReadOutputAsync()
                {
                    while (true)
                    {
                        var line = await p.StandardOutput.ReadLineAsync(token);
                        if (line == null) break;
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        var colonIndex = line.IndexOf(':');
                        if (colonIndex < 0) continue;

                        var key = line[..colonIndex].Trim();
                        var value = line[(colonIndex + 1)..].Trim();
                        if (string.IsNullOrWhiteSpace(value)) continue;

                        if (key.Contains("Publisher", StringComparison.OrdinalIgnoreCase) ||
                            key.Contains("Herausgeber", StringComparison.OrdinalIgnoreCase))
                        {
                            publisher = value;
                        }
                        else if (key.Contains("Download Size", StringComparison.OrdinalIgnoreCase) ||
                                 key.Contains("Downloadgröße", StringComparison.OrdinalIgnoreCase))
                        {
                            // Echte Downloadgröße hat immer Vorrang und darf nicht
                            // durch eine später gefundene Installationsgröße
                            // überschrieben werden.
                            size = value;
                            foundDownloadSize = true;
                        }
                        else if (!foundDownloadSize &&
                                 (key.Contains("Größe", StringComparison.OrdinalIgnoreCase) ||
                                  (key.Contains("Size", StringComparison.OrdinalIgnoreCase) &&
                                   !key.Contains("Installer", StringComparison.OrdinalIgnoreCase))))
                        {
                            // Fallback: irgendeine andere Größenangabe (z.B.
                            // Installationsgröße), falls keine Downloadgröße
                            // gefunden wird - besser als "N/A".
                            size = value;
                        }
                    }
                }

                // Fehlerausgabe jetzt mitschreiben statt zu verwerfen, damit man bei
                // durchgängigem "N/A" im Log nachvollziehen kann, woran es lag.
                var errorOutput = new StringBuilder();
                async Task ReadErrorAsync()
                {
                    while (true)
                    {
                        var line = await p.StandardError.ReadLineAsync(token);
                        if (line == null) break;
                        if (!string.IsNullOrWhiteSpace(line))
                            errorOutput.AppendLine(line);
                    }
                }

                await Task.WhenAll(ReadOutputAsync(), ReadErrorAsync(), p.WaitForExitAsync(token));

                if (publisher == "N/A" && size == "N/A")
                {
                    var errText = errorOutput.ToString().Trim();
                    Logger.Log($"winget show '{packageId}' lieferte weder Herausgeber noch Größe " +
                               $"(ExitCode {p.ExitCode}){(string.IsNullOrEmpty(errText) ? "" : $": {errText}")}");
                }

            }
            catch (OperationCanceledException) when (!_startupCancellation.IsCancellationRequested && timeoutCancellation.IsCancellationRequested)
            {
                Logger.Log($"winget show '{packageId}' nach 30 Sekunden abgebrochen.");
            }
            catch (OperationCanceledException)
            {
                Logger.Log($"winget show '{packageId}' beim Beenden von WinVora abgebrochen.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"GetWingetDetailsAsync({packageId})", ex);
            }

            // Auch nach einem WinGet-Fehler oder Timeout noch die lokale Registry
            // als Fallback verwenden. Diese Daten sind sofort verfügbar und
            // verhindern unnötige „Unbekannt“-Angaben.
            var registryDetails = InstalledProgramsService.FindDetailsForPackage(
                installedPrograms, packageName, packageId);

            if (publisher == "N/A" && !string.IsNullOrWhiteSpace(registryDetails.Publisher))
                publisher = registryDetails.Publisher;

            if (size == "N/A" && !string.IsNullOrWhiteSpace(registryDetails.Size))
                size = registryDetails.Size;

            bool en = Localization.CurrentLanguage == "en";
            return (publisher == "N/A" ? (en ? "Unknown" : "Unbekannt") : publisher,
                    size == "N/A" ? (en ? "Unknown" : "Unbekannt") : size);
        }
    }
}
