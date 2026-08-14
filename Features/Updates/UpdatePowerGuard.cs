using System;
using System.Management;
using System.Runtime.InteropServices;

namespace WinVora
{
    internal sealed record BatteryUpdateState(bool HasBattery, int ChargePercent, bool Charging);

    internal sealed class UpdatePowerGuard : IDisposable
    {
        [Flags]
        private enum ExecutionState : uint
        {
            Continuous = 0x80000000,
            SystemRequired = 0x00000001
        }

        [DllImport("kernel32.dll")]
        private static extern ExecutionState SetThreadExecutionState(ExecutionState state);

        private bool _active;

        public void Start()
        {
            _active = SetThreadExecutionState(ExecutionState.Continuous | ExecutionState.SystemRequired) != 0;
            Logger.Log(_active
                ? "Energiesparschutz für Programm-Updates aktiviert."
                : "Energiesparschutz konnte nicht aktiviert werden.");
        }

        public void Dispose()
        {
            if (!_active) return;
            SetThreadExecutionState(ExecutionState.Continuous);
            _active = false;
            Logger.Log("Energiesparschutz für Programm-Updates beendet.");
        }

        public static BatteryUpdateState ReadBatteryState()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "root\\CIMV2", "SELECT EstimatedChargeRemaining, BatteryStatus FROM Win32_Battery");
                using var results = searcher.Get();
                foreach (ManagementObject battery in results)
                {
                    int charge = Convert.ToInt32(battery["EstimatedChargeRemaining"] ?? 100);
                    int status = Convert.ToInt32(battery["BatteryStatus"] ?? 0);
                    bool charging = status is 2 or 6 or 7 or 8 or 9 or 11;
                    return new BatteryUpdateState(true, charge, charging);
                }
            }
            catch (Exception ex)
            {
                Logger.LogErrorOnce("Akkustand vor Programm-Update prüfen", ex);
            }
            return new BatteryUpdateState(false, 100, false);
        }
    }
}
