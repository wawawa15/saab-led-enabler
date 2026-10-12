using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace SaabDidTool.Services
{
    public sealed class UpdateInfo
    {
        public string Version { get; set; } = string.Empty;
        public string ReleaseNotes { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public string AssetFileName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Checks the GitHub "latest release" for this repo and, if its tag is
    /// newer than CurrentVersion, offers to download the installer asset
    /// and run it (the app should exit right after launching the
    /// installer, so it isn't holding its own files open while the
    /// installer tries to replace them).
    /// </summary>
    public static class UpdateChecker
    {
        // Bump this before tagging a new GitHub release.
        public const string CurrentVersion = "1.0.5";

        // owner/repo on GitHub - update if the repo is renamed/moved.
        private const string GitHubOwner = "wawawa15";
        private const string GitHubRepo = "saab-led-enabler";

        private static readonly HttpClient Http = new HttpClient();

        static UpdateChecker()
        {
            // The GitHub API requires a User-Agent header or it returns 403.
            Http.DefaultRequestHeaders.UserAgent.ParseAdd("Saab93NGLedEnabler-UpdateChecker");
            Http.Timeout = TimeSpan.FromSeconds(10);
        }

        /// <summary>
        /// Returns null if no update is available or the check failed
        /// (network, rate limit, wrong repo name, etc.). Pass a logger to
        /// see exactly what happened instead of it failing silently.
        /// </summary>
        public static async Task<UpdateInfo> CheckForUpdateAsync(Action<string> log = null)
        {
            log ??= _ => { };
            try
            {
                string url = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";
                log($"Checking for updates: {url}");
                string json = await Http.GetStringAsync(url);

                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;

                string tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
                string versionText = tag.TrimStart('v', 'V');
                log($"Latest GitHub release tag: '{tag}' (parsed as '{versionText}')");

                if (!Version.TryParse(versionText, out Version latest))
                {
                    log($"Could not parse '{versionText}' as a version (expected X.Y or X.Y.Z) - skipping update check.");
                    return null;
                }
                if (!Version.TryParse(CurrentVersion, out Version current))
                {
                    current = new Version(0, 0, 0);
                }
                log($"Current version: {current}  |  Latest release: {latest}");
                if (latest <= current)
                {
                    log("Already up to date.");
                    return null;
                }

                string notes = root.TryGetProperty("body", out JsonElement bodyEl) ? (bodyEl.GetString() ?? string.Empty) : string.Empty;

                // Find the installer asset - prefers a name ending in
                // "Setup.exe" (matches the Inno Setup output naming).
                JsonElement assets = root.GetProperty("assets");
                JsonElement? chosen = null;
                foreach (JsonElement asset in assets.EnumerateArray())
                {
                    string name = asset.GetProperty("name").GetString() ?? string.Empty;
                    if (name.EndsWith("Setup.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        chosen = asset;
                        break;
                    }
                }
                if (chosen == null && assets.GetArrayLength() > 0)
                {
                    chosen = assets[0]; // fall back to the first asset if none matches the naming convention
                }
                if (chosen == null)
                {
                    log("Release has no downloadable assets attached - nothing to offer.");
                    return null;
                }

                string assetName = chosen.Value.GetProperty("name").GetString() ?? "update.exe";
                log($"Update found: v{versionText}, asset '{assetName}'.");

                return new UpdateInfo
                {
                    Version = versionText,
                    ReleaseNotes = notes,
                    DownloadUrl = chosen.Value.GetProperty("browser_download_url").GetString() ?? string.Empty,
                    AssetFileName = assetName,
                };
            }
            catch (Exception ex)
            {
                // Offline, rate-limited, wrong repo name, no releases yet,
                // etc. - the app should never interrupt the user with a
                // dialog for this, but it's still logged for diagnosis.
                log("Update check failed: " + ex.Message);
                return null;
            }
        }

        /// <summary>Downloads the installer to a temp file and returns its local path.</summary>
        public static async Task<string> DownloadInstallerAsync(UpdateInfo info)
        {
            string tempPath = Path.Combine(Path.GetTempPath(), info.AssetFileName);
            byte[] data = await Http.GetByteArrayAsync(info.DownloadUrl);
            await File.WriteAllBytesAsync(tempPath, data);
            return tempPath;
        }

        /// <summary>Launches the downloaded installer. Call Application.Exit() right after this.</summary>
        public static void RunInstaller(string installerPath)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = installerPath,
                UseShellExecute = true, // lets Windows show the UAC prompt if the installer needs elevation
            });
        }
    }
}
