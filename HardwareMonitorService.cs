using System;
using System.Linq;
using LibreHardwareMonitor.Hardware;

namespace WinVora
{
    public class HardwareReadings
    {
        public double? GpuLoadPercent { get; set; }
        public double? CpuTemperature { get; set; }
        public double? GpuTemperature { get; set; }
    }

    public static class HardwareMonitorService
    {
        private static Computer? _computer;
        private static readonly object Lock = new();

        private static Computer GetComputer()
        {
            if (_computer == null)
            {
                lock (Lock)
                {
                    _computer ??= new Computer
                    {
                        IsCpuEnabled = true,
                        IsGpuEnabled = true
                    };

                    if (!_computer.Hardware.Any())
                        _computer.Open();
                }
            }

            return _computer;
        }

        // Öffnet LibreHardwareMonitor frühzeitig im Hintergrund (kann beim
        // allerersten Mal spürbar dauern - Treiber laden, Hardware erkennen).
        // Wird beim App-Start früh aufgerufen, damit der erste echte
        // GPU-/Temperatur-Wert nicht extra lange auf sich warten lässt.
        public static void WarmUp()
        {
            try { GetComputer(); }
            catch { /* Sensoren evtl. nicht zugänglich - kein Problem hier */ }
        }

        // Liest GPU-Auslastung sowie CPU-/GPU-Temperatur aus. Läuft am besten
        // in einem Hintergrund-Thread (Task.Run), da das Sensor-Update kurz
        // dauern kann. Manche Sensoren (v.a. Temperatur) sind ohne Admin-Rechte
        // oder auf manchen Systemen/Treibern schlicht nicht zugänglich - dann
        // bleiben die jeweiligen Werte einfach null ("nicht verfügbar"),
        // statt irgendwas zu erfinden.
        public static HardwareReadings GetReadings()
        {
            var result = new HardwareReadings();

            try
            {
                var computer = GetComputer();

                foreach (var hardware in computer.Hardware)
                {
                    hardware.Update();
                    foreach (var sub in hardware.SubHardware)
                        sub.Update();

                    if (hardware.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
                    {
                        var load = hardware.Sensors.FirstOrDefault(s =>
                                       s.SensorType == SensorType.Load &&
                                       s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                                   ?? hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load);

                        if (load?.Value != null)
                            result.GpuLoadPercent = Math.Round(load.Value.Value, 1);

                        var gpuTemp = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature);
                        if (gpuTemp?.Value != null)
                            result.GpuTemperature = Math.Round(gpuTemp.Value.Value, 1);
                    }

                    if (hardware.HardwareType == HardwareType.Cpu)
                    {
                        var cpuTemp = hardware.Sensors.FirstOrDefault(s =>
                                          s.SensorType == SensorType.Temperature &&
                                          (s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
                                           s.Name.Contains("Average", StringComparison.OrdinalIgnoreCase)))
                                      ?? hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature);

                        if (cpuTemp?.Value != null)
                            result.CpuTemperature = Math.Round(cpuTemp.Value.Value, 1);
                    }
                }
            }
            catch
            {
                // Sensoren nicht zugänglich (z.B. fehlende Admin-Rechte oder
                // nicht unterstützte Hardware) - Werte bleiben einfach null.
            }

            return result;
        }

        public static void Shutdown()
        {
            try { _computer?.Close(); } catch { }
        }
    }
}