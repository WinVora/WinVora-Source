namespace WinVora
{
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
