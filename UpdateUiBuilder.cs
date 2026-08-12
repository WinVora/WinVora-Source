namespace WinVora
{
    internal static class UpdateUiBuilder
    {
        public static string VersionSummary(WingetPackage package, bool english) => english
            ? $"CURRENT   {package.Version}     NEW   {package.Available}"
            : $"AKTUELL   {package.Version}     NEU   {package.Available}";

        public static string TechnicalDetails(WingetPackage package, bool english) =>
            (english ? "Package ID: " : "Paket-ID: ") + package.Id + "\n" +
            (english ? "Source: " : "Quelle: ") + package.Source;
    }
}
