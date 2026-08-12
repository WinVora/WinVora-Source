using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace WinVora
{
    public static class IconExtractionService
    {
        // Liest das Icon einer .exe/.ico-Datei aus und liefert es als PNG-Bytes zurück.
        // Läuft am besten in einem Hintergrund-Thread (Task.Run), da Icon.ExtractAssociatedIcon
        // und das Kodieren als PNG etwas Zeit brauchen können.
        public static byte[]? ExtractIconPngBytes(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return null;

            try
            {
                using Icon? icon = Icon.ExtractAssociatedIcon(filePath);
                if (icon == null) return null;

                using var bitmap = icon.ToBitmap();
                using var ms = new MemoryStream();
                bitmap.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
            catch
            {
                // Manche Dateien (z.B. reine .dll ohne Icon-Ressource, oder fehlende
                // Zugriffsrechte) lassen sich nicht auslesen - dann einfach kein Icon.
                return null;
            }
        }
    }
}
