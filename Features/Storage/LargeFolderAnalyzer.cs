using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WinVora
{
    internal sealed record LargeFolderResult(
        string Path,
        long SizeBytes,
        bool IsAccessible = true,
        string? Error = null);

    internal static class LargeFolderAnalyzer
    {
        public static Task<IReadOnlyList<LargeFolderResult>> AnalyzeAsync(
            CancellationToken token,
            IProgress<int>? progress = null,
            IProgress<LargeFolderResult>? resultProgress = null) =>
            Task.Run<IReadOnlyList<LargeFolderResult>>(() =>
            {
                string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string[] roots =
                {
                    Path.Combine(profile, "Downloads"),
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                    Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
                };
                var candidates = roots.Where(Directory.Exists)
                    // Nur echte Unterordner anzeigen. Der Stammordner wie
                    // "Dokumente" würde lediglich alle darunter bereits
                    // aufgeführten Größen noch einmal zusammenfassen.
                    .SelectMany(EnumerateDirectories)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select((path, index) =>
                    {
                        var (size, accessible, error) = GetSize(path, token);
                        var result = new LargeFolderResult(path, size, accessible, error);
                        progress?.Report(index + 1);
                        resultProgress?.Report(result);
                        return result;
                    })
                    .Where(item => item.SizeBytes > 0 || !item.IsAccessible)
                    .ToList();
                return candidates.Where(item => item.IsAccessible)
                    .OrderByDescending(item => item.SizeBytes)
                    .Take(10)
                    .Concat(candidates.Where(item => !item.IsAccessible).Take(3))
                    .ToList();
            }, token);

        private static IEnumerable<string> EnumerateDirectories(string root)
        {
            try { return SystemAccess.FileSystem.EnumerateDirectories(root).Where(path => !StorageService.IsReparsePoint(path)).ToArray(); }
            catch (UnauthorizedAccessException) { return Array.Empty<string>(); }
            catch (IOException) { return Array.Empty<string>(); }
        }

        private static (long Size, bool Accessible, string? Error) GetSize(string root, CancellationToken token)
        {
            long total = 0;
            bool accessible = true;
            string? error = null;
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                string current = pending.Pop();
                if (StorageService.IsReparsePoint(current)) continue;
                try
                {
                    foreach (string file in SystemAccess.FileSystem.EnumerateFiles(current))
                    {
                        token.ThrowIfCancellationRequested();
                        try { total += new FileInfo(file).Length; }
                        catch (IOException) { accessible = false; error ??= "Datei nicht erreichbar"; }
                        catch (UnauthorizedAccessException) { accessible = false; error ??= "Zugriff verweigert"; }
                    }
                    foreach (string directory in SystemAccess.FileSystem.EnumerateDirectories(current))
                        if (!StorageService.IsReparsePoint(directory)) pending.Push(directory);
                }
                catch (IOException) { accessible = false; error ??= "Ordner nicht erreichbar"; }
                catch (UnauthorizedAccessException) { accessible = false; error ??= "Zugriff verweigert"; }
            }
            return (total, accessible, error);
        }
    }
}
