using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace WinVora
{
    public class UpdateInfo
    {
        public string Version { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public string AssetName { get; set; } = "";
        public string Sha256 { get; set; } = "";
        public string ReleaseNotes { get; set; } = "";
        public bool IsPrerelease { get; set; }
    }

    public static class UpdateService
    {
        // Öffentliches Downloads-Repo, nicht das private Quellcode-Repo.
        private const string ReleasesApiUrl = "https://api.github.com/repos/WinVora/WinVora-Releases/releases/latest";
        private const string AllReleasesApiUrl = "https://api.github.com/repos/WinVora/WinVora-Releases/releases?per_page=20";

        public static async Task<UpdateInfo?> CheckForUpdateAsync(string currentVersion, bool includePrerelease = false)
        {
            using var client = new HttpClient();
            // GitHub verlangt zwingend einen User-Agent-Header, sonst kommt 403.
            client.DefaultRequestHeaders.UserAgent.ParseAdd("WinVora-UpdateChecker");
            client.Timeout = TimeSpan.FromSeconds(15);

            var json = await client.GetStringAsync(includePrerelease ? AllReleasesApiUrl : ReleasesApiUrl);
            using var doc = JsonDocument.Parse(json);
            if (includePrerelease)
            {
                foreach (var release in doc.RootElement.EnumerateArray())
                {
                    var candidate = ParseRelease(release, currentVersion, allowPrerelease: true);
                    if (candidate != null) return candidate;
                }
                return null;
            }
            return ParseRelease(doc.RootElement, currentVersion, allowPrerelease: false);
        }

        private static UpdateInfo? ParseRelease(JsonElement root, string currentVersion, bool allowPrerelease)
        {
            if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean()) return null;
            bool prerelease = root.TryGetProperty("prerelease", out var prereleaseProperty) && prereleaseProperty.GetBoolean();
            if (prerelease && !allowPrerelease) return null;

            var tagName = root.GetProperty("tag_name").GetString() ?? "";
            var latestVersion = tagName.TrimStart('v', 'V');

            if (!IsNewerVersion(latestVersion, currentVersion))
                return null;

            string? downloadUrl = null;
            string? assetName = null;
            string? sha256 = null;

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
                        if (asset.TryGetProperty("digest", out var digestProperty))
                        {
                            var digest = digestProperty.GetString();
                            if (digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true)
                                sha256 = digest[7..];
                        }
                        break;
                    }
                }
            }

            if (downloadUrl == null || assetName == null)
                throw new InvalidDataException("Für die neue Version wurde kein passender Installer veröffentlicht.");

            var notes = root.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? "" : "";

            return new UpdateInfo
            {
                Version = latestVersion,
                DownloadUrl = downloadUrl,
                AssetName = assetName,
                Sha256 = sha256 ?? "",
                ReleaseNotes = notes,
                IsPrerelease = prerelease
            };
        }

        internal static bool IsNewerVersion(string latest, string current)
        {
            try
            {
                // GitHub-Tags dürfen z. B. "0.8.5-beta.1" heißen, während
                // Windows-Dateiversionen rein numerisch bleiben müssen.
                string latestNumeric = latest.Split('-', '+')[0];
                string currentNumeric = current.Split('-', '+')[0];
                var v1 = new Version(latestNumeric);
                var v2 = new Version(currentNumeric);
                return v1 > v2;
            }
            catch
            {
                // Falls sich eine der Versionsnummern nicht sauber parsen lässt,
                // vorsichtshalber nur bei echtem Unterschied ein Update anbieten.
                return !string.Equals(latest, current, StringComparison.OrdinalIgnoreCase);
            }
        }

        public static async Task<string> DownloadUpdateAsync(UpdateInfo update, IProgress<DownloadProgressInfo>? progress)
        {
            if (!Uri.TryCreate(update.DownloadUrl, UriKind.Absolute, out var downloadUri) ||
                downloadUri.Scheme != Uri.UriSchemeHttps ||
                !downloadUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Die Update-Adresse ist nicht vertrauenswürdig.");
            }

            if (string.IsNullOrWhiteSpace(update.Sha256) || update.Sha256.Length != 64 ||
                !update.Sha256.All(Uri.IsHexDigit))
                throw new InvalidOperationException("GitHub liefert keine gültige SHA-256-Prüfsumme für dieses Update.");

            var safeAssetName = Path.GetFileName(update.AssetName);
            if (string.IsNullOrWhiteSpace(safeAssetName) ||
                !safeAssetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Der Dateiname des Updates ist ungültig.");
            }

            // Eindeutiger Dateiname pro Versuch - verhindert, dass ein noch
            // laufender Installer aus einem vorherigen Update-Versuch die Datei
            // für den nächsten Download blockiert ("wird von einem anderen
            // Prozess verwendet").
            var uniqueName = $"{Path.GetFileNameWithoutExtension(safeAssetName)}_{Guid.NewGuid():N}{Path.GetExtension(safeAssetName)}";
            var tempPath = Path.Combine(Path.GetTempPath(), uniqueName);

            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("WinVora-UpdateChecker");
            client.Timeout = TimeSpan.FromMinutes(5);

            Logger.Log($"Update-Download startet: {update.DownloadUrl}");

            try
            {
                using var response = await client.GetAsync(downloadUri, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var finalUri = response.RequestMessage?.RequestUri;
                if (finalUri == null || finalUri.Scheme != Uri.UriSchemeHttps ||
                    !(finalUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
                      finalUri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException("Das Update wurde auf ein nicht vertrauenswürdiges Downloadziel umgeleitet.");
                }

                var totalBytes = response.Content.Headers.ContentLength ?? -1;
                Logger.Log($"Update-Download: HTTP {(int)response.StatusCode}, Content-Length: {(totalBytes > 0 ? totalBytes.ToString() : "unbekannt")}");

                await using var contentStream = await response.Content.ReadAsStreamAsync();
                await using (var fileStream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[81920];
                    long totalRead = 0;
                    int bytesRead;

                    while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                        totalRead += bytesRead;
                        progress?.Report(new DownloadProgressInfo(totalRead, totalBytes));
                    }

                    Logger.Log($"Update-Download abgeschlossen: {totalRead} Bytes -> {tempPath}");
                }

                await using var verificationStream = File.OpenRead(tempPath);
                var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(verificationStream));
                if (!actualHash.Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "Die SHA-256-Prüfsumme des Downloads stimmt nicht mit GitHub überein.");
                }

                Logger.Log($"Update-Prüfsumme bestätigt: {actualHash.ToLowerInvariant()}");
                return tempPath;
            }
            catch
            {
                try
                {
                    if (File.Exists(tempPath)) File.Delete(tempPath);
                }
                catch (Exception cleanupException)
                {
                    Logger.LogError("Temporäre Update-Datei konnte nicht gelöscht werden", cleanupException);
                }

                throw;
            }
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

        public static void CleanupOldDownloads()
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(Path.GetTempPath(), "WinVora-Setup-*.exe"))
                {
                    try
                    {
                        if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-1))
                            File.Delete(file);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError($"Alte Update-Datei konnte nicht entfernt werden: {file}", ex);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("Alte Update-Downloads konnten nicht geprüft werden", ex);
            }
        }
    }

    // Fortschrittsinformation für den Update-Download. TotalBytes ist -1,
    // falls der Server keine Content-Length mitliefert.
    public readonly record struct DownloadProgressInfo(long BytesReceived, long TotalBytes);
}
