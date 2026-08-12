using System;
using System.Diagnostics;

namespace WinVora
{
    internal static class CoreLogicSelfTests
    {
        [Conditional("DEBUG")]
        public static void Run()
        {
            var package = WingetTableParser.Parse(
                "Demo App            Vendor.Demo       1.0       2.0       winget",
                new[] { 0, 20, 38, 48, 58 });
            Debug.Assert(package?.Id == "Vendor.Demo");
            Debug.Assert(package?.Available == "2.0");

            Debug.Assert(WingetErrorTranslator.ContainsRestartRequired("Restart required"));
            Debug.Assert(!WingetErrorTranslator.ContainsRestartRequired("Installation complete"));

            var settings = new AppSettings
            {
                StartupPage = "invalid",
                LiveUpdateIntervalSeconds = 99,
                Language = "xx",
                AnimationMode = "invalid"
            };
            settings.Validate();
            Debug.Assert(settings.StartupPage == "Übersicht");
            Debug.Assert(settings.LiveUpdateIntervalSeconds == 2);
            Debug.Assert(settings.Language == "de");
            Debug.Assert(settings.AnimationMode is "Full" or "Reduced" or "Off");

            Debug.Assert(InstalledProgramsService.TrySplitCommand(
                "\"C:\\Program Files (x86)\\Steam\\steam.exe\" steam://uninstall/123",
                out string steamExe, out string steamArgs));
            Debug.Assert(steamExe.EndsWith("steam.exe", StringComparison.OrdinalIgnoreCase));
            Debug.Assert(steamArgs == "steam://uninstall/123");
        }
    }
}
