// Copyright (c) 2024 NetBind Pro Contributors
// Licensed under the MIT License. See LICENSE in the project root for details.

using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace NetBindPro.Core.Services;

/// <summary>Information about an available update.</summary>
public sealed record UpdateInfo(string Version, string Notes, string DownloadUrl, string AssetName);

/// <summary>
/// Checks GitHub Releases for a newer NetBind Pro and can download + launch the
/// installer. The repository is public, so the releases API needs no token.
/// </summary>
public sealed class UpdateService
{
    private const string LatestReleaseApi =
        "https://api.github.com/repos/fintechcoding/netbindpro/releases/latest";

    private readonly ILogger<UpdateService>? _logger;

    public UpdateService(ILogger<UpdateService>? logger = null) => _logger = logger;

    /// <summary>The running app's version (e.g. "2.3.2").</summary>
    public static Version CurrentVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version is { } v
            ? new Version(v.Major, v.Minor, v.Build < 0 ? 0 : v.Build)
            : new Version(0, 0, 0);

    /// <summary>
    /// Returns details of a newer release, or <c>null</c> if up to date / on error.
    /// </summary>
    public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("NetBindPro-Updater");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            var release = await http.GetFromJsonAsync<GitHubRelease>(LatestReleaseApi, ct).ConfigureAwait(false);
            if (release?.TagName == null) return null;

            var latest = ParseVersion(release.TagName);
            if (latest == null || latest <= CurrentVersion) return null;

            var asset = release.Assets?.FirstOrDefault(a =>
                a.Name != null && a.Name.EndsWith("Setup.exe", StringComparison.OrdinalIgnoreCase));
            if (asset?.BrowserDownloadUrl == null) return null;

            _logger?.LogInformation("Update available: {Latest} (current {Current})", latest, CurrentVersion);
            return new UpdateInfo(latest.ToString(), release.Body ?? string.Empty,
                asset.BrowserDownloadUrl, asset.Name!);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Update check failed");
            return null;
        }
    }

    /// <summary>
    /// Downloads the update installer to %TEMP% and launches it (elevates via the shell).
    /// The caller should exit the app so the installer can replace the files.
    /// </summary>
    public async Task<(bool Success, string Message)> DownloadAndRunAsync(
        UpdateInfo update, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        try
        {
            var dest = Path.Combine(Path.GetTempPath(), update.AssetName);

            progress?.Report("Downloading update…");
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("NetBindPro-Updater");
                using var resp = await http.GetAsync(update.DownloadUrl,
                    HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                await using var fs = File.Create(dest);
                await resp.Content.CopyToAsync(fs, ct).ConfigureAwait(false);
            }

            progress?.Report("Launching installer…");
            Process.Start(new ProcessStartInfo
            {
                FileName        = dest,
                UseShellExecute = true, // elevates the installer
            });
            return (true, "Installer launched. NetBind Pro will close to apply the update.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Update download/run failed");
            return (false, $"Update failed: {ex.Message}");
        }
    }

    private static Version? ParseVersion(string tag)
    {
        var cleaned = tag.TrimStart('v', 'V').Split('-')[0];
        return Version.TryParse(cleaned, out var v) ? v : null;
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("body")]     public string? Body { get; set; }
        [JsonPropertyName("assets")]   public List<GitHubAsset>? Assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]                 public string? Name { get; set; }
        [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; set; }
    }
}
