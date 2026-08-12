namespace WinVora
{
    internal static class StorageUiBuilder
    {
        public static string DeleteSelectionText(int count, long bytes, bool english) => count == 0
            ? Localization.T("Storage.DeleteSelected")
            : english
                ? $"Delete {count} · {StorageService.FormatBytes(bytes)}"
                : $"{count} löschen · {StorageService.FormatBytes(bytes)}";
    }
}
