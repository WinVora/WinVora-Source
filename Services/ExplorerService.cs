using System;
using System.Diagnostics;
using System.IO;

namespace WinVora
{
    internal enum ExplorerOpenResult { Opened, Missing, Failed }

    internal static class ExplorerService
    {
        public static ExplorerOpenResult OpenFolder(string path)
        {
            try
            {
                string fullPath = Path.GetFullPath(path);
                if (!Directory.Exists(fullPath)) return ExplorerOpenResult.Missing;
                Process.Start(new ProcessStartInfo { FileName = fullPath, UseShellExecute = true });
                return ExplorerOpenResult.Opened;
            }
            catch (Exception ex)
            {
                Logger.LogError("Ordner im Explorer öffnen", ex);
                return ExplorerOpenResult.Failed;
            }
        }

        public static ExplorerOpenResult SelectFile(string path)
        {
            try
            {
                string fullPath = Path.GetFullPath(path);
                if (!File.Exists(fullPath)) return ExplorerOpenResult.Missing;
                var startInfo = new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    UseShellExecute = true
                };
                startInfo.ArgumentList.Add("/select," + fullPath);
                Process.Start(startInfo);
                return ExplorerOpenResult.Opened;
            }
            catch (Exception ex)
            {
                Logger.LogError("Datei im Explorer auswählen", ex);
                return ExplorerOpenResult.Failed;
            }
        }
    }
}
