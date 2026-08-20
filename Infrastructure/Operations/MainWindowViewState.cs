using System;
using System.Collections.Generic;

namespace WinVora
{
    internal sealed class MainWindowViewState
    {
        private readonly Dictionary<string, bool> _updateSelection = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> _storageSelection = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> _uninstallSelection = new(StringComparer.OrdinalIgnoreCase);

        public bool IsUpdateSelected(string packageId) => GetOrDefault(_updateSelection, packageId, true);
        public bool IsStorageSelected(string categoryKey) => GetOrDefault(_storageSelection, categoryKey, false);
        public bool IsProgramSelected(string identity) => GetOrDefault(_uninstallSelection, identity, false);

        public void SetUpdateSelected(string packageId, bool selected) => Set(_updateSelection, packageId, selected);
        public void SetStorageSelected(string categoryKey, bool selected) => Set(_storageSelection, categoryKey, selected);
        public void SetProgramSelected(string identity, bool selected) => Set(_uninstallSelection, identity, selected);

        public void RetainUpdates(ISet<string> currentIds) => Retain(_updateSelection, currentIds);
        public void RetainStorage(ISet<string> currentKeys) => Retain(_storageSelection, currentKeys);
        public void RetainPrograms(ISet<string> currentIdentities) => Retain(_uninstallSelection, currentIdentities);

        private static bool GetOrDefault(Dictionary<string, bool> values, string key, bool defaultValue) =>
            !string.IsNullOrWhiteSpace(key) && values.TryGetValue(key, out bool value) ? value : defaultValue;

        private static void Set(Dictionary<string, bool> values, string key, bool value)
        {
            if (!string.IsNullOrWhiteSpace(key)) values[key] = value;
        }

        private static void Retain(Dictionary<string, bool> values, ISet<string> current)
        {
            foreach (string staleKey in new List<string>(values.Keys))
                if (!current.Contains(staleKey)) values.Remove(staleKey);
        }
    }
}
