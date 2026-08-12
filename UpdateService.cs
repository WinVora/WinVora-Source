using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace WinVora
{
    public class UpdateInfo
    {
        public string Version { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public string AssetName { get; set; } = "";
        public string ReleaseNotes { get; set; } = "";
    }

    public static class UpdateService
    {
        // Öffentliches Downloads-Repo, nicht das private Quellcode-Repo.
        private const string ReleasesApiUrl = "https://api.github.com/repos/WinVora/WinVora-Releases/releases/latest";

        public static async Task<UpdateInfo?> CheckForUpdateAsync(string currentVersion)
        {
            using var client = new HttpClient();
            // GitHub verlangt zwingend einen User-Agent-Header, sonst kommt 403.
            client.DefaultRequestHeaders.UserAgent.ParseAdd("WinVora-UpdateChecker");
            client.Timeout = TimeSpan.FromSeconds(15);

            var json = await client.GetStringAsync(ReleasesApiUrl);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tagName = root.GetProperty("tag_name").GetString() ?? "";
            var latestVersion = tagName.TrimStart('v', 'V');

            if (!IsNewerVersion(latestVersion, currentVersion))
                return null;

            string? downloadUrl = null;
            string? assetName = null;

            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    if (name.Contains("Setup", StringComparison.OrdinalIgnoreCase) &&
                        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = asset.GetProperty("browser_download_url").GetString();
                        assetName = name;
                        break;
                    }
                }
            }

            if (downloadUrl == null || assetName == null)
                return null; // Release existiert, aber kein passender Installer als Anhang gefunden

            var notes = root.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? "" : "";

            return new UpdateInfo
            {
                Version = latestVersion,
                DownloadUrl = downloadUrl,
                AssetName = assetName,
                ReleaseNotes = notes
            };
        }

        private static bool IsNewerVersion(string latest, string current)
        {
            try
            {
                var v1 = new Version(latest);
                var v2 = new Version(current);
                return v1 > v2;
            }
            catch
            {
                // Falls sich eine der Versionsnummern nicht sauber parsen lässt,
                // vorsichtshalber nur bei echtem Unterschied ein Update anbieten.
                return !string.Equals(latest, current, StringComparison.OrdinalIgnoreCase);
            }
        }

        public static async Task<string> DownloadUpdateAsync(string url, string assetName, IProgress<DownloadProgressInfo>? progress)
        {
            // Eindeutiger Dateiname pro Versuch - verhindert, dass ein noch
            // laufender Installer aus einem vorherigen Update-Versuch die Datei
            // für den nächsten Download blockiert ("wird von einem anderen
            // Prozess verwendet").
            var uniqueName = $"{Path.GetFileNameWithoutExtension(assetName)}_{Guid.NewGuid():N}{Path.GetExtension(assetName)}";
            var tempPath = Path.Combine(Path.GetTempPath(), uniqueName);

            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("WinVora-UpdateChecker");
            client.Timeout = TimeSpan.FromMinutes(5);

            Logger.Log($"Update-Download startet: {url}");

            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            Logger.Log($"Update-Download: HTTP {(int)response.StatusCode}, Content-Length: {(totalBytes > 0 ? totalBytes.ToString() : "unbekannt")}");

            await using var contentStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[81920];
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                totalRead += bytesRead;

                // Wird IMMER gemeldet (auch ohne bekannte Gesamtgröße), damit in
                // der UI sichtbar ist, dass tatsächlich Daten ankommen - vorher
                // blieb der Text bei unbekannter Content-Length einfach stehen,
                // was wie ein Hänger aussah, obwohl im Hintergrund weiterlief.
                progress?.Report(new DownloadProgressInfo(totalRead, totalBytes));
            }

            Logger.Log($"Update-Download abgeschlossen: {totalRead} Bytes -> {tempPath}");

            return tempPath;
        }

        // Startet den heruntergeladenen Installer im komplett stillen Modus
        // (/VERYSILENT) - kein Assistenten-Fenster, keine Klicks nötig. Nur der
        // Windows-UAC-Hinweis für die Admin-Rechte lässt sich nicht umgehen,
        // das ist Windows-Sicherheit, keine Installer-Einstellung.
        // Der Installer übernimmt dank "CloseApplications=yes" automatisch das
        // Schließen von WinVora und aktualisiert die bestehende Installation
        // in-place, und startet WinVora danach automatisch neu.
        public static void RunInstaller(string installerPath)
        {
            var psi = new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART",
                UseShellExecute = true
            };

            Process.Start(psi);
        }
    }

    // Fortschrittsinformation für den Update-Download. TotalBytes ist -1,
    // falls der Server keine Content-Length mitliefert.
    public readonly record struct DownloadProgressInfo(long BytesReceived, long TotalBytes);
}