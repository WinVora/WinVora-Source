using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;

namespace WinVora
{
    /// <summary>
    /// Eng begrenzte Schnittstelle zwischen der normal gestarteten Oberfläche
    /// und einem kurzlebigen, per UAC erhöhten WinVora-Hilfsprozess.
    /// Der Helper akzeptiert ausschließlich bekannte Storage-Kategorieschlüssel.
    /// </summary>
    internal static class ElevatedActionService
    {
        private const string StorageDeleteArgument = "--elevated-storage-delete";
        private const int UserCancelledExitCode = 1223;
        private const int InvalidRequestExitCode = 2;

        public static bool IsHelperInvocation(IReadOnlyList<string> arguments) =>
            arguments.Any(argument => string.Equals(argument, StorageDeleteArgument, StringComparison.Ordinal));

        public static async Task<int> RunStorageDeleteElevatedAsync(IEnumerable<string> categoryKeys)
        {
            try
            {
                string? executable = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(executable))
                    throw new InvalidOperationException("Eigener Programmpfad konnte nicht ermittelt werden.");

                string[] keys = categoryKeys
                    .Where(key => !string.IsNullOrWhiteSpace(key))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (!TryValidateStorageKeys(keys, out _))
                    throw new InvalidOperationException("Die angeforderte Admin-Aktion enthält ungültige Kategorien.");

                var startInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    UseShellExecute = true,
                    Verb = "runas"
                };
                startInfo.ArgumentList.Add(StorageDeleteArgument);
                startInfo.ArgumentList.Add(string.Join(';', keys));

                using Process? helper = Process.Start(startInfo);
                if (helper == null) return -1;
                await helper.WaitForExitAsync();
                return helper.ExitCode;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == UserCancelledExitCode)
            {
                Logger.Log("Elevierte Storage-Löschung vom Nutzer abgebrochen (UAC verweigert).");
                return UserCancelledExitCode;
            }
            catch (Exception ex)
            {
                Logger.LogError("Elevierten Storage-Helper starten", ex);
                return -1;
            }
        }

        public static async Task<int> ExecuteHelperAsync(IReadOnlyList<string> arguments)
        {
            if (!IsProcessElevated())
            {
                Logger.Log("Admin-Helper ohne erhöhtes Zugriffstoken abgelehnt.");
                return InvalidRequestExitCode;
            }

            int actionIndex = -1;
            for (int index = 0; index < arguments.Count; index++)
            {
                if (string.Equals(arguments[index], StorageDeleteArgument, StringComparison.Ordinal))
                {
                    actionIndex = index;
                    break;
                }
            }

            if (actionIndex < 0 || actionIndex + 1 >= arguments.Count || actionIndex + 2 != arguments.Count)
            {
                Logger.Log("Ungültiger Aufruf des Admin-Helpers abgelehnt.");
                return InvalidRequestExitCode;
            }

            string[] keys = arguments[actionIndex + 1]
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (!TryValidateStorageKeys(keys, out List<StorageCategory> categories))
            {
                Logger.Log("Admin-Helper hat unbekannte oder nicht administrative Storage-Kategorien abgelehnt.");
                return InvalidRequestExitCode;
            }

            bool succeeded = true;
            foreach (StorageCategory category in categories)
            {
                var (success, message) = await StorageService.DeleteCategoryAsync(category).ConfigureAwait(false);
                Logger.Log($"Elevierte Löschung '{category.Name}': {(success ? "OK" : "FEHLER")} - {message}");
                succeeded &= success;
            }
            return succeeded ? 0 : 1;
        }

        internal static bool TryValidateStorageKeys(IEnumerable<string> requestedKeys, out List<StorageCategory> categories)
        {
            categories = new List<StorageCategory>();
            string[] keys = requestedKeys.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (keys.Length == 0 || keys.Length > 32 || keys.Any(key => key.Length > 64)) return false;

            var available = StorageService.GetCategoryDefinitions()
                .Where(category => category.RequiresAdmin)
                .ToDictionary(category => category.Key, StringComparer.OrdinalIgnoreCase);
            foreach (string key in keys)
            {
                if (!available.TryGetValue(key, out StorageCategory? category)) return false;
                categories.Add(category);
            }
            return true;
        }

        private static bool IsProcessElevated()
        {
            try
            {
                using WindowsIdentity identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch (Exception ex)
            {
                Logger.LogErrorOnce("Admin-Helper-Token prüfen", ex);
                return false;
            }
        }
    }
}
