using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Diagnostics;

namespace WinVora.Tests;

[TestClass]
public sealed class CoreLogicTests
{
    private sealed class FakeProcessRunner(ProcessRunResult result) : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(
            ProcessStartInfo startInfo,
            TimeSpan timeout,
            CancellationToken cancellationToken = default) => Task.FromResult(result);
    }

    [DataTestMethod]
    [DataRow("0.8.5", "0.8.5-beta.2", true)]
    [DataRow("0.8.5-beta.2", "0.8.5-beta.1", true)]
    [DataRow("0.8.5-beta.1", "0.8.5-beta.2", false)]
    [DataRow("0.8.5-beta.3", "0.8.5-beta.2", true)]
    [DataRow("0.8.5-beta.2", "0.8.5-beta.3", false)]
    [DataRow("0.8.4", "0.8.5", false)]
    public void VersionComparisonOrdersStableAndPrereleaseCorrectly(
        string latest,
        string current,
        bool expected)
    {
        Assert.AreEqual(expected, UpdateService.IsNewerVersion(latest, current));
    }

    [DataTestMethod]
    [DataRow("Aktiv", "Aktiv", "Active")]
    [DataRow("Active", "Partial/Inactive", "Problem")]
    [DataRow("Unbekannt", "Aktiv", "Unknown")]
    [DataRow("Deaktiviert", "Aktiv", "Problem")]
    public void SecurityStatusIsEvaluatedConservatively(
        string antivirus,
        string firewall,
        string expected)
    {
        Assert.AreEqual(expected, SecurityStatusEvaluator.Evaluate(antivirus, firewall).ToString());
    }

    [TestMethod]
    public void StorageAllowlistAcceptsOnlyUpgradeRoots()
    {
        string root = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows))!;
        Assert.IsTrue(StorageService.IsProtectedFolderPathAllowlisted(Path.Combine(root, "Windows.old")));
        Assert.IsTrue(StorageService.IsProtectedFolderPathAllowlisted(Path.Combine(root, "$WINDOWS.~BT")));
        Assert.IsFalse(StorageService.IsProtectedFolderPathAllowlisted(Path.Combine(root, "Windows")));
        Assert.IsFalse(StorageService.IsProtectedFolderPathAllowlisted(Path.Combine(root, "Windows.old", "Users")));
    }

    [TestMethod]
    public void DiagnosticSanitizerRemovesPersonalData()
    {
        var snapshot = new SystemInfoSnapshot
        {
            ComputerName = "DESKTOP-PRIVATE",
            UserName = "Private User",
            SerialNumber = "SERIAL-123"
        };
        string source = @"C:\Users\Private User\Desktop\file.txt DESKTOP-PRIVATE SERIAL-123 private@example.com 192.168.1.20";
        string sanitized = DiagnosticReportBuilder.Sanitize(source, snapshot);

        Assert.IsFalse(sanitized.Contains("Private User", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(sanitized.Contains("DESKTOP-PRIVATE", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(sanitized.Contains("SERIAL-123", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(sanitized.Contains("private@example.com", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(sanitized.Contains("192.168.1.20", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void WingetParserReadsACompleteRow()
    {
        int[] columns = [0, 24, 50, 66, 82];
        string line = "Example App".PadRight(columns[1]) +
                      "Example.App".PadRight(columns[2] - columns[1]) +
                      "1.0".PadRight(columns[3] - columns[2]) +
                      "2.0".PadRight(columns[4] - columns[3]) +
                      "winget";

        WingetPackage? package = WingetTableParser.Parse(line, columns);
        Assert.IsNotNull(package);
        Assert.AreEqual("Example.App", package.Id);
        Assert.AreEqual("1.0", package.Version);
        Assert.AreEqual("2.0", package.Available);
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task WingetDiscoveryRejectsEveryNonZeroExitCode()
    {
        IProcessRunner original = SystemAccess.ProcessRunner;
        try
        {
            SystemAccess.ProcessRunner = new FakeProcessRunner(
                new ProcessRunResult(42, "partial table", string.Empty, TimedOut: false));
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => WingetDiscoveryService.GetUpgradesAsync(CancellationToken.None));
        }
        finally
        {
            SystemAccess.ProcessRunner = original;
        }
    }

    [TestMethod]
    public void NeutralSecurityStatesDriveTheOverallResult()
    {
        Assert.AreEqual(
            SecurityHealthState.Problem,
            SecurityStatusEvaluator.Evaluate(
                SecurityComponentState.Active,
                SecurityComponentState.Partial));
    }
}
