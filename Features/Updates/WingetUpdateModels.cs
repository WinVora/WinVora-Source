namespace WinVora
{
    public class WingetPackage
    {
        public string Name { get; set; } = "";
        public string Id { get; set; } = "";
        public string Version { get; set; } = "";
        public string Available { get; set; } = "";
        public string Source { get; set; } = "";
        public string Publisher { get; set; } = "Unbekannt";
        public string DownloadSize { get; set; } = "Unbekannt";
    }

    internal enum WingetUpdatePhase
    {
        Downloading,
        Installing,
        Waiting
    }

    internal enum WingetUpdateStatus
    {
        Successful,
        Failed,
        Cancelled,
        RestartRequired
    }

    internal sealed record WingetUpdateProgress(
        WingetUpdatePhase Phase,
        string Text,
        double? Percent,
        string? Speed = null,
        string? Eta = null);

    internal sealed record WingetUpdateResult(
        WingetUpdateStatus Status,
        int ExitCode,
        string Message,
        bool RestartRequired);
}
