using Microsoft.Win32;
using System;

namespace WinVora
{
    internal static class RestartDetectionService
    {
        public static bool IsRestartPending()
        {
            try
            {
                using var cbs = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending");
                if (cbs != null) return true;

                using var windowsUpdate = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired");
                if (windowsUpdate != null) return true;

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
