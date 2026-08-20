using Microsoft.Win32;
using System;

namespace WinVora
{
    internal static class RestartDetectionService
    {
        // Strenge Prüfung für sichtbare Nutzerhinweise. Ein allein gesetztes
        // PendingFileRenameOperations bleibt nach Treiber-/Installer-Vorgängen
        // häufig lange bestehen und ist kein verlässlicher Neustartgrund.
        public static bool IsExplicitWindowsRestartPending()
        {
            try
            {
                using var cbs = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending");
                if (cbs != null) return true;

                using var windowsUpdate = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired");
                return windowsUpdate != null;
            }
            catch (Exception ex)
            {
                Logger.LogError("Expliziter Windows-Neustartstatus konnte nicht gelesen werden", ex);
                return false;
            }
        }

        public static bool IsRestartPending()
        {
            try
            {
                if (IsExplicitWindowsRestartPending()) return true;

                using var sessionManager = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\Session Manager");
                return sessionManager?.GetValue("PendingFileRenameOperations") != null;
            }
            catch (Exception ex)
            {
                Logger.LogError("Windows-Neustartstatus konnte nicht gelesen werden", ex);
                return false;
            }
        }
    }
}
