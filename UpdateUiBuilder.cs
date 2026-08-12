namespace WinVora
{
    internal static class UpdateUiBuilder
    {
        public static string VersionSummary(WingetPackage package, bool english) => english
            ? $"CURRENT   {package.Version}     NEW   {package.Available}"
            : $"AKTUELL   {package.Version}     NEU   {package.Available}";

        public static string TechnicalDetails(WingetPackage package, bool english) =>
            (english ? "Install source: " : "Installationsquelle: ") + package.Source + "\n" +
            (english ? "Publisher: " : "Herausgeber: ") + package.Publisher + "\n" +
            (english ? "Download size: " : "Downloadgröße: ") + package.DownloadSize + "\n" +
            (english ? "Version change: " : "Versionsänderung: ") + package.Version + " → " + package.Available + "\n" +
            (english ? "Change notes: WinGet does not provide release notes for this package." : "Änderungsinformationen: WinGet stellt für dieses Paket keine Versionshinweise bereit.") + "\n\n" +
            (english ? "Package ID: " : "Paket-ID: ") + package.Id;
    }
}
