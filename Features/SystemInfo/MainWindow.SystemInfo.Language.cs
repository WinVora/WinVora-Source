using System;
using System.Threading;
using System.Threading.Tasks;

namespace WinVora
{
    public sealed partial class MainWindow
    {
        private int _systemInfoLanguageRefreshVersion;

        private void RequestSystemInfoLanguageRefresh()
        {
            int requestVersion = ++_systemInfoLanguageRefreshVersion;
            _ = ReloadSystemInfoForLanguageAsync(requestVersion);
        }

        private async Task ReloadSystemInfoForLanguageAsync(int requestVersion)
        {
            try
            {
                // Eine bereits laufende WMI-Abfrage darf zuerst kontrolliert
                // auslaufen. Nur der jüngste Sprachwechsel startet anschließend
                // einen neuen Snapshot; schnelle Mehrfachwechsel erzeugen daher
                // keine parallelen Vollabfragen.
                while (_isLoadingSnapshot)
                {
                    await Task.Delay(100, _startupCancellation.Token);
                    if (requestVersion != _systemInfoLanguageRefreshVersion) return;
                }

                if (requestVersion != _systemInfoLanguageRefreshVersion) return;
                SystemInfoProvider.InvalidateLocalizedCache();
                _cachedSnapshot = null;
                _fullSystemSnapshotLoaded = false;
                _securityDetailsLoaded = false;
                await LoadSystemSnapshotIfNeededAsync(
                    Localization.T("Common.LoadingSystemInfo"),
                    Localization.T("System.LoadError"));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Logger.LogError("Systeminfo nach Sprachwechsel neu laden", ex);
            }
        }
    }
}
