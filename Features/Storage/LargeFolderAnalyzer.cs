using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WinVora
{
    internal sealed record LargeFolderResult(string Path, long SizeBytes);

    internal static class LargeFolderAnalyzer
    {
        public static Task<IReadOnlyList<LargeFolderResult>> AnalyzeAsync(
            CancellationToken token,
            IProgress<int>? progress = null) =>
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
                        var result = new LargeFolderResult(path, GetSize(path, token));
                        progress?.Report(index + 1);
                        return result;
                    })
                    .Where(item => item.SizeBytes > 0)
                    .OrderByDescending(item => item.SizeBytes)
                    .Take(10)
                    .ToList();
                return candidates;
            }, token);

        private static IEnumerable<string> EnumerateDirectories(string root)
        {
            try { return Directory.EnumerateDirectories(root).ToArray(); }
            catch (UnauthorizedAccessException) { return Array.Empty<string>(); }
            catch (IOException) { return Array.Empty<string>(); }
        }

        private static long GetSize(string root, CancellationToken token)
        {
            long total = 0;
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                string current = pending.Pop();
                try
                {
                    foreach (string file in Directory.EnumerateFiles(current))
                    {
                        token.ThrowIfCancellationRequested();
                        try { total += new FileInfo(file).Length; }
                        catch (IOException) { }
                        catch (UnauthorizedAccessException) { }
                    }
                    foreach (string directory in Directory.EnumerateDirectories(current))
                        pending.Push(directory);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return total;
        }
    }
}
