using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace WinVora
{
    internal sealed record HardwareTelemetrySnapshot(
        double CpuPercent,
        double RamPercent,
        double RamUsedGb,
        double RamTotalGb,
        HardwareReadings Sensors,
        DateTime CapturedUtc);

    /// <summary>
    /// Einziger Einstiegspunkt für laufende CPU-, RAM-, GPU- und Sensorwerte.
    /// Gleichzeitige Aufrufer teilen sich eine Messung, statt LibreHardwareMonitor
    /// und PerformanceCounter mehrfach parallel zu aktualisieren.
    /// </summary>
    internal static class HardwareTelemetryService
    {
        private static readonly SemaphoreSlim RefreshGate = new(1, 1);
        private static readonly object CacheLock = new();
        private static HardwareTelemetrySnapshot? _cached;
        private static readonly TimeSpan SensorCacheLifetime = TimeSpan.FromSeconds(2);

        public static void WarmUp()
        {
            SystemInfoProvider.WarmUpCpuCounter();
            HardwareMonitorService.WarmUp();
        }

        public static async Task<HardwareTelemetrySnapshot> GetSnapshotAsync(
            bool refreshSensors,
            CancellationToken cancellationToken = default)
        {
            await RefreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // SemaphoreSlim verhindert nur Überlappungen; es verschiebt die
                // eigentliche Messung nicht automatisch vom aufrufenden Thread.
                // PerformanceCounter und LibreHardwareMonitor laufen deshalb
                // ausdrücklich im ThreadPool.
                return await Task.Run(
                    () => CaptureSnapshot(refreshSensors, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                RefreshGate.Release();
            }
        }

        private static HardwareTelemetrySnapshot CaptureSnapshot(
            bool refreshSensors,
            CancellationToken cancellationToken)
        {
            var timer = Stopwatch.StartNew();
            cancellationToken.ThrowIfCancellationRequested();
            var (cpu, ram, _, ramUsedGb, ramTotalGb) = SystemInfoProvider.GetLiveUsage();
            HardwareReadings? sensors = null;
            lock (CacheLock)
            {
                bool fresh = _cached != null && DateTime.UtcNow - _cached.CapturedUtc < SensorCacheLifetime;
                if (!refreshSensors && fresh)
                    sensors = _cached!.Sensors.Clone();
            }

            cancellationToken.ThrowIfCancellationRequested();
            sensors ??= HardwareMonitorService.GetReadings();
            cancellationToken.ThrowIfCancellationRequested();

            var snapshot = new HardwareTelemetrySnapshot(
                cpu, ram, ramUsedGb, ramTotalGb, sensors.Clone(), DateTime.UtcNow);
            lock (CacheLock) _cached = snapshot;
            timer.Stop();
            if (timer.ElapsedMilliseconds >= 250)
                Logger.Log($"Langsame Hardwaretelemetrie: {timer.ElapsedMilliseconds} ms (Sensoren: {refreshSensors})");
            return Clone(snapshot);
        }

        public static HardwareTelemetrySnapshot GetSnapshot(
            bool refreshSensors,
            CancellationToken cancellationToken = default) =>
            GetSnapshotAsync(refreshSensors, cancellationToken).GetAwaiter().GetResult();

        private static HardwareTelemetrySnapshot Clone(HardwareTelemetrySnapshot snapshot) =>
            snapshot with { Sensors = snapshot.Sensors.Clone() };

        public static void Shutdown()
        {
            lock (CacheLock) _cached = null;
            HardwareMonitorService.Shutdown();
        }
    }
}
