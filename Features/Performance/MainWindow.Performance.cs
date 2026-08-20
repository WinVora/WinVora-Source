using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WinVora
{
    public sealed partial class MainWindow
    {
        private CancellationTokenSource? _performanceAnalysisCancellation;

        private async void Performance_Click(object sender, RoutedEventArgs e)
        {
            if (!TrySetPage("Performance")) return;
            await AnalyzePerformanceAsync();
        }

        private async void PerformanceRefresh_Click(object sender, RoutedEventArgs e) =>
            await AnalyzePerformanceAsync();

        private async Task AnalyzePerformanceAsync()
        {
            _performanceAnalysisCancellation?.Cancel();
            _performanceAnalysisCancellation?.Dispose();
            _performanceAnalysisCancellation = CancellationTokenSource.CreateLinkedTokenSource(_startupCancellation.Token);
            CancellationToken token = _performanceAnalysisCancellation.Token;

            PerformanceRefreshButton.IsEnabled = false;
            PerformanceSummaryText.Text = Localization.T("Performance.Analyzing");
            PerformanceIntroText.Text = Localization.T("Performance.Intro");
            PerformanceCheckedAtText.Text = string.Empty;
            PerformanceFindingsPanel.Children.Clear();
            PerformanceFindingsPanel.Children.Add(new ProgressRing
            {
                IsActive = true,
                Width = 34,
                Height = 34,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 18, 0, 0)
            });
            SetGlobalStatus(Localization.T("Performance.Analyzing"));

            try
            {
                var snapshot = (_cachedSnapshot ?? await SystemInfoProvider.GetFullSnapshotAsync(token)).Clone();
                await SystemInfoProvider.RefreshSectionAsync(snapshot, SystemInfoSection.Security, token);
                var result = await PerformanceAnalysisService.AnalyzeAsync(
                    _wingetRows.Count, _securityHealthState, snapshot, token);
                if (token.IsCancellationRequested || _currentPageKey != "Performance") return;
                RenderPerformanceResult(result);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                Logger.Log("PC-Check abgebrochen.");
            }
            catch (Exception ex)
            {
                Logger.LogError("PC-Check", ex);
                PerformanceFindingsPanel.Children.Clear();
                PerformanceSummaryText.Text = Localization.T("Performance.AnalysisFailed");
                PerformanceFindingsPanel.Children.Add(MakeEmptyState(
                    "\uEA39", Localization.T("Performance.AnalysisFailed"),
                    Localization.T("Performance.Intro"), Localization.T("Performance.Analyze"), AnalyzePerformanceAsync));
            }
            finally
            {
                if (_performanceAnalysisCancellation?.Token == token)
                {
                    _performanceAnalysisCancellation.Dispose();
                    _performanceAnalysisCancellation = null;
                    PerformanceRefreshButton.IsEnabled = true;
                    SetGlobalStatus(null);
                }
            }
        }

        private void RenderPerformanceResult(PerformanceAnalysisResult result)
        {
            PerformanceFindingsPanel.Children.Clear();
            PerformanceRefreshButton.Content = Localization.T("Performance.Analyze");
            PerformanceIntroText.Text = Localization.T("Performance.Intro");
            PerformanceCheckedAtText.Text = Localization.F("Performance.CheckedAt", result.CheckedAt.ToString("g"));

            var recommendations = result.Findings.Where(f => f.Severity != PerformanceFindingSeverity.Info).ToList();
            var notes = result.Findings.Where(f => f.Severity == PerformanceFindingSeverity.Info).ToList();
            PerformanceSummaryText.Text = Localization.F("Performance.Summary", recommendations.Count, result.ChecksCompleted);

            if (recommendations.Count == 0 && notes.Count == 0)
            {
                PerformanceFindingsPanel.Children.Add(MakeEmptyState(
                    "\uE73E", Localization.T("Performance.AllGood"),
                    Localization.T("Performance.AllGoodDetail")));
            }
            else
            {
                if (recommendations.Count > 0)
                {
                    PerformanceFindingsPanel.Children.Add(CreatePerformanceHeading("Performance.RecommendationsHeading"));
                    foreach (var finding in recommendations)
                        PerformanceFindingsPanel.Children.Add(CreatePerformanceFindingCard(finding));
                }
                if (notes.Count > 0)
                {
                    PerformanceFindingsPanel.Children.Add(CreatePerformanceHeading("Performance.NotesHeading"));
                    foreach (var finding in notes)
                        PerformanceFindingsPanel.Children.Add(CreatePerformanceFindingCard(finding));
                }
            }
            if (result.PassedChecks.Count > 0)
            {
                PerformanceFindingsPanel.Children.Add(CreatePerformanceHeading("Performance.PassedHeading"));
                var passedPanel = new StackPanel { Spacing = 8 };
                foreach (string passed in result.PassedChecks)
                    passedPanel.Children.Add(new TextBlock { Text = $"✓  {passed}", FontSize = 13, TextWrapping = TextWrapping.Wrap });
                PerformanceFindingsPanel.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(14), Padding = new Thickness(18),
                    Background = (SolidColorBrush)RootGrid.Resources["AppOverlay18"], Child = passedPanel
                });
            }
        }


        private TextBlock CreatePerformanceHeading(string key) => new()
        {
            Text = Localization.T(key), FontSize = 17,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, 12, 0, 2)
        };

        private Border CreatePerformanceFindingCard(PerformanceFinding finding)
        {
            string brushKey = finding.Severity switch
            {
                PerformanceFindingSeverity.Critical => "AppErrorBrush",
                PerformanceFindingSeverity.Warning => "AppWarningBrush",
                _ => "AppAccentBrushLight"
            };
            var color = (SolidColorBrush)RootGrid.Resources[brushKey];
            var card = new Border
            {
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(18),
                Background = (SolidColorBrush)RootGrid.Resources["AppOverlay18"],
                BorderBrush = color,
                BorderThickness = new Thickness(3, 0, 0, 0)
            };

            var grid = new Grid { ColumnSpacing = 16 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var iconHost = new Border
            {
                Width = 42,
                Height = 42,
                CornerRadius = new CornerRadius(12),
                Background = (SolidColorBrush)RootGrid.Resources["AppAccentOverlay10"],
                Child = new FontIcon { Glyph = finding.Glyph, FontSize = 19, Foreground = color }
            };
            grid.Children.Add(iconHost);

            var text = new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock
            {
                Text = finding.Title,
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });
            text.Children.Add(new TextBlock
            {
                Text = finding.Description,
                FontSize = 13,
                Foreground = (SolidColorBrush)RootGrid.Resources["AppMutedForegroundBrush"],
                TextWrapping = TextWrapping.Wrap
            });
            text.Children.Add(new TextBlock
            {
                Text = Localization.F("Performance.RecommendedAction", finding.ActionText),
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = color,
                TextWrapping = TextWrapping.Wrap
            });
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            var action = new Button
            {
                Content = finding.ActionText,
                Padding = new Thickness(12, 7, 12, 7),
                VerticalAlignment = VerticalAlignment.Center
            };
            action.Click += async (_, __) => await OpenPerformanceTargetAsync(finding.TargetPage);
            Grid.SetColumn(action, 2);
            grid.Children.Add(action);

            card.Child = grid;
            return card;
        }

        private async Task OpenPerformanceTargetAsync(string targetPage)
        {
            if (targetPage == "WindowsUpdate")
            {
                OpenPerformanceTool("ms-settings:windowsupdate");
                return;
            }
            if (targetPage == "Restart")
            {
                bool confirmed = await ConfirmAsync(
                    Localization.T("Performance.RestartConfirmTitle"),
                    Localization.T("Performance.RestartConfirmMessage"),
                    Localization.T("Performance.RestartNow"),
                    respectDeleteConfirmationSetting: false);
                if (!confirmed) return;
                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "shutdown.exe",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    startInfo.ArgumentList.Add("/r");
                    startInfo.ArgumentList.Add("/t");
                    startInfo.ArgumentList.Add("0");
                    SystemAccess.Process.Start(startInfo);
                }
                catch (Exception ex)
                {
                    Logger.LogError("Windows-Neustart starten", ex);
                    ShowInfo(Localization.T("Performance.RestartFailed"), InfoBarSeverity.Error);
                }
                return;
            }

            if (PerformanceActionCatalog.TryGetExternalTool(targetPage, out PerformanceToolCommand tool))
            {
                OpenPerformanceTool(tool.FileName, tool.Arguments);
                return;
            }

            if (!TrySetPage(targetPage)) return;
            switch (targetPage)
            {
                case "Autostart": await RenderAutostartPageAsync(); break;
                case "Storage": await LoadStorage(); break;
                case "Updates": await LoadWinget(forceRefresh: false); break;
                case "Uninstall": await LoadInstalledPrograms(); break;
            }
        }

        private void OpenPerformanceTool(string fileName, string? arguments = null)
        {
            try
            {
                var startInfo = new ProcessStartInfo(fileName)
                {
                    UseShellExecute = true
                };
                if (!string.IsNullOrWhiteSpace(arguments))
                    startInfo.Arguments = arguments;
                SystemAccess.Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                Logger.LogError($"PC-Check-Aktion öffnen: {fileName}", ex);
                ShowInfo(Localization.T("Performance.ActionFailed"), InfoBarSeverity.Error);
            }
        }
    }
}
