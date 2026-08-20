using System;
using System.Linq;
using System.Collections.Generic;
using LibreHardwareMonitor.Hardware;

namespace WinVora
{
    public class HardwareReadings
    {
        public double? GpuLoadPercent { get; set; }
        public double? CpuTemperature { get; set; }
        public double? GpuTemperature { get; set; }
        public double? CpuClockMhz { get; set; }
        public double? CpuPowerWatts { get; set; }
        public double? GpuPowerWatts { get; set; }
        public List<HardwareStorageReading> Storage { get; } = new();
        public List<HardwareFanReading> Fans { get; } = new();

        internal HardwareReadings Clone()
        {
            var copy = new HardwareReadings
            {
                GpuLoadPercent = GpuLoadPercent,
                CpuTemperature = CpuTemperature,
                GpuTemperature = GpuTemperature,
                CpuClockMhz = CpuClockMhz,
                CpuPowerWatts = CpuPowerWatts,
                GpuPowerWatts = GpuPowerWatts
            };
            copy.Storage.AddRange(Storage);
            copy.Fans.AddRange(Fans);
            return copy;
        }
    }

    public sealed record HardwareStorageReading(string Name, double? Temperature, double? RemainingLife);
    public sealed record HardwareFanReading(string Name, double Rpm);

    public static class HardwareMonitorService
    {
        private static Computer? _computer;
        private static readonly object Lock = new();
        private static readonly HashSet<string> FailedHardwareUpdates =
            new(StringComparer.OrdinalIgnoreCase);

        private static Computer GetComputer()
        {
            if (_computer == null)
            {
                lock (Lock)
                {
                    _computer ??= new Computer
                    {
                        IsCpuEnabled = true,
                        IsGpuEnabled = true,
                        IsMemoryEnabled = false,
                        IsMotherboardEnabled = false,
                        IsControllerEnabled = false,
                        IsNetworkEnabled = false,
                        IsStorageEnabled = false,
                        IsPowerMonitorEnabled = false
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
            catch (Exception ex) { Logger.LogErrorOnce("Hardwaremonitor initialisieren", ex); }
        }

        // Liest GPU-Auslastung sowie CPU-/GPU-Temperatur aus. Läuft am besten
        // in einem Hintergrund-Thread (Task.Run), da das Sensor-Update kurz
        // dauern kann. Manche Sensoren (v.a. Temperatur) sind ohne Admin-Rechte
        // oder auf manchen Systemen/Treibern schlicht nicht zugänglich - dann
        // bleiben die jeweiligen Werte einfach null ("nicht verfügbar"),
        // statt irgendwas zu erfinden.
        public static HardwareReadings GetReadings(bool extended = false)
        {
            var result = new HardwareReadings();

            try
            {
                lock (Lock)
                {
                    if (!extended)
                    {
                        ReadComputer(GetComputer(), result);
                        return result;
                    }

                    // Laufwerke, Mainboard und Lüfter braucht nur der PC-Check.
                    // Diese teure Hardwareliste bleibt deshalb nicht während der
                    // gesamten App-Laufzeit im Speicher.
                    var computer = new Computer
                    {
                        IsCpuEnabled = true,
                        IsGpuEnabled = true,
                        IsMemoryEnabled = true,
                        IsMotherboardEnabled = true,
                        IsControllerEnabled = true,
                        IsNetworkEnabled = true,
                        IsStorageEnabled = true,
                        IsPowerMonitorEnabled = true
                    };
                    try
                    {
                        computer.Open();
                        ReadComputer(computer, result);
                    }
                    finally
                    {
                        computer.Close();
                    }
                }
            }
            catch (Exception ex)
            {
                // Sensoren nicht zugänglich (z.B. fehlende Admin-Rechte oder
                // nicht unterstützte Hardware) - Werte bleiben einfach null.
                Logger.LogErrorOnce("Hardwaremonitor auslesen", ex);
            }

            return result;
        }

        private static void ReadComputer(Computer computer, HardwareReadings result)
        {
            foreach (var hardware in computer.Hardware)
            {
                if (!TryUpdateHardware(hardware))
                    continue;

                foreach (var sub in hardware.SubHardware)
                    TryUpdateHardware(sub);

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

                    var gpuPower = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Power);
                    if (gpuPower?.Value > 0.5f) result.GpuPowerWatts = Math.Round(gpuPower.Value.Value, 1);
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

                    var cpuClock = hardware.Sensors.Where(s => s.SensorType == SensorType.Clock && s.Value != null)
                        .Select(s => (double)s.Value!.Value).DefaultIfEmpty().Average();
                    if (cpuClock > 0) result.CpuClockMhz = Math.Round(cpuClock, 0);
                    var cpuPower = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Power && s.Value != null);
                    if (cpuPower?.Value > 0.5f) result.CpuPowerWatts = Math.Round(cpuPower.Value.Value, 1);
                }

                if (hardware.HardwareType == HardwareType.Storage)
                {
                    double? temperature = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature)?.Value;
                    double? life = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Level &&
                        s.Name.Contains("life", StringComparison.OrdinalIgnoreCase))?.Value;
                    result.Storage.Add(new HardwareStorageReading(hardware.Name, temperature, life));
                }

                foreach (var sensor in hardware.Sensors.Concat(hardware.SubHardware.SelectMany(s => s.Sensors)))
                    if ((hardware.HardwareType is HardwareType.Motherboard or HardwareType.SuperIO or HardwareType.Cpu) &&
                        sensor.SensorType == SensorType.Fan && sensor.Value is float rpm)
                        result.Fans.Add(new HardwareFanReading($"{hardware.Name} – {sensor.Name}", Math.Round(rpm, 0)));
            }
        }

        private static bool TryUpdateHardware(IHardware hardware)
        {
            string identifier = hardware.Identifier.ToString();
            if (FailedHardwareUpdates.Contains(identifier))
                return false;

            try
            {
                hardware.Update();
                return true;
            }
            catch (Exception ex)
            {
                FailedHardwareUpdates.Add(identifier);
                Logger.LogErrorOnce($"Hardwaresensor aktualisieren ({hardware.Name}, {identifier})", ex);
                return false;
            }
        }

        public static void Shutdown()
        {
            try
            {
                lock (Lock)
                {
                    _computer?.Close();
                    _computer = null;
                    FailedHardwareUpdates.Clear();
                }
            }
            catch (Exception ex) { Logger.LogErrorOnce("Hardwaremonitor schließen", ex); }
        }
    }
}
