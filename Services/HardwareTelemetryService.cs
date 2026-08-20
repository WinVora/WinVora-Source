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
        private static readonly TimeSpan NormalSensorCacheLifetime = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan SlowSystemSensorCacheLifetime = TimeSpan.FromSeconds(5);
        private static bool _slowSensorMode;

        public static void WarmUp()
        {
            SystemInfoProvider.WarmUpCpuCounter();
        }

        public static async Task<HardwareTelemetrySnapshot> GetSnapshotAsync(
            bool refreshSensors,
            CancellationToken cancellationToken = default,
            bool extendedSensors = false)
        {
            await RefreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // SemaphoreSlim verhindert nur Überlappungen; es verschiebt die
                // eigentliche Messung nicht automatisch vom aufrufenden Thread.
                // PerformanceCounter und LibreHardwareMonitor laufen deshalb
                // ausdrücklich im ThreadPool.
                return await Task.Run(
                    () => CaptureSnapshot(refreshSensors, extendedSensors, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                RefreshGate.Release();
            }
        }

        private static HardwareTelemetrySnapshot CaptureSnapshot(
            bool refreshSensors,
            bool extendedSensors,
            CancellationToken cancellationToken)
        {
            var timer = Stopwatch.StartNew();
            cancellationToken.ThrowIfCancellationRequested();
            var (cpu, ram, _, ramUsedGb, ramTotalGb) = SystemInfoProvider.GetLiveUsage();
            HardwareReadings? sensors = null;
            lock (CacheLock)
            {
                TimeSpan lifetime = _slowSensorMode
                    ? SlowSystemSensorCacheLifetime
                    : NormalSensorCacheLifetime;
                bool fresh = _cached != null && DateTime.UtcNow - _cached.CapturedUtc < lifetime;
                // Auch ein Sensor-Tick darf einen ausreichend frischen Wert
                // wiederverwenden. Auf langsameren PCs wird die native
                // Hardwarebibliothek dadurch höchstens alle fünf Sekunden
                // aufgerufen, CPU/RAM werden trotzdem bei jedem Tick erneuert.
                if (!extendedSensors && fresh)
                    sensors = _cached!.Sensors.Clone();
            }

            cancellationToken.ThrowIfCancellationRequested();
            // CPU/RAM sind sofort verfügbar. Die deutlich schwerere native
            // Sensorbibliothek wird erst bei einem echten Sensor-Tick geladen.
            // So zeigt WinVora das Dashboard, ohne den Start für GPU/Temperatur
            // oder Mainboard-Sensoren zu blockieren.
            if (sensors == null && refreshSensors)
            {
                var sensorTimer = Stopwatch.StartNew();
                sensors = HardwareMonitorService.GetReadings(extendedSensors);
                sensorTimer.Stop();
                if (sensorTimer.ElapsedMilliseconds >= 500 && !_slowSensorMode)
                {
                    _slowSensorMode = true;
                    Logger.Log($"Langsame Sensorhardware erkannt ({sensorTimer.ElapsedMilliseconds} ms); " +
                        "Sensorintervall wird automatisch auf mindestens fünf Sekunden begrenzt.");
                }
                else if (sensorTimer.ElapsedMilliseconds <= 150 && _slowSensorMode)
                {
                    _slowSensorMode = false;
                    Logger.Log("Sensorhardware reagiert wieder schnell; normales Sensorintervall wird verwendet.");
                }
            }
            sensors ??= new HardwareReadings();
            cancellationToken.ThrowIfCancellationRequested();

            var snapshot = new HardwareTelemetrySnapshot(
                cpu, ram, ramUsedGb, ramTotalGb, sensors.Clone(), DateTime.UtcNow);
            lock (CacheLock) _cached = snapshot;
            timer.Stop();
            if (timer.ElapsedMilliseconds >= 250)
            {
                using var process = Process.GetCurrentProcess();
                Logger.Log($"Langsame Hardwaretelemetrie: {timer.ElapsedMilliseconds} ms " +
                    $"(Sensoren: {refreshSensors}, erweitert: {extendedSensors}, " +
                    $"Working Set: {process.WorkingSet64 / 1024d / 1024d:0.0} MB, " +
                    $"verwaltet: {GC.GetTotalMemory(false) / 1024d / 1024d:0.0} MB)");
            }
            return Clone(snapshot);
        }

        public static HardwareTelemetrySnapshot GetSnapshot(
            bool refreshSensors,
            CancellationToken cancellationToken = default,
            bool extendedSensors = false) =>
            GetSnapshotAsync(refreshSensors, cancellationToken, extendedSensors).GetAwaiter().GetResult();

        private static HardwareTelemetrySnapshot Clone(HardwareTelemetrySnapshot snapshot) =>
            snapshot with { Sensors = snapshot.Sensors.Clone() };

        public static void Shutdown()
        {
            lock (CacheLock)
            {
                _cached = null;
                _slowSensorMode = false;
            }
            HardwareMonitorService.Shutdown();
        }
    }
}
