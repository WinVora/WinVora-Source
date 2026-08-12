using Microsoft.UI.Xaml;
using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage.Pickers;

namespace WinVora
{
    internal static class ReportExportService
    {
        public static async Task<bool> SaveTextAsync(Window owner, string suggestedName, string content)
        {
            var picker = new FileSavePicker { SuggestedFileName = suggestedName };
            picker.FileTypeChoices.Add("Text", new[] { ".txt" });
            WinRT.Interop.InitializeWithWindow.Initialize(
                picker, WinRT.Interop.WindowNative.GetWindowHandle(owner));
            var file = await picker.PickSaveFileAsync();
            if (file == null) return false;
            await File.WriteAllTextAsync(file.Path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            return true;
        }

        public static async Task<bool> SaveJsonAsync(Window owner, string suggestedName, string content)
        {
            var picker = new FileSavePicker { SuggestedFileName = suggestedName };
            picker.FileTypeChoices.Add("JSON", new[] { ".json" });
            WinRT.Interop.InitializeWithWindow.Initialize(
                picker, WinRT.Interop.WindowNative.GetWindowHandle(owner));
            var file = await picker.PickSaveFileAsync();
            if (file == null) return false;
            await File.WriteAllTextAsync(file.Path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            return true;
        }

        public static async Task<bool> SaveCsvAsync(Window owner, string suggestedName, string content)
        {
            var picker = new FileSavePicker { SuggestedFileName = suggestedName };
            picker.FileTypeChoices.Add("CSV", new[] { ".csv" });
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(owner));
            var file = await picker.PickSaveFileAsync();
            if (file == null) return false;
            await File.WriteAllTextAsync(file.Path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            return true;
        }

        public static async Task<bool> SaveSupportZipAsync(Window owner, string suggestedName, string report)
        {
            var picker = new FileSavePicker { SuggestedFileName = suggestedName };
            picker.FileTypeChoices.Add("ZIP", new[] { ".zip" });
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(owner));
            var file = await picker.PickSaveFileAsync();
            if (file == null) return false;

            await using var stream = new FileStream(file.Path, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920, useAsync: true);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);
            var entry = archive.CreateEntry("WinVora-Supportbericht.txt", CompressionLevel.Optimal);
            await using var entryStream = entry.Open();
            await using var writer = new StreamWriter(entryStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            await writer.WriteAsync(report);
            return true;
        }
    }
}
