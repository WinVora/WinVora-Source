using Microsoft.UI.Xaml;
using System;
using System.IO;
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
    }
}
