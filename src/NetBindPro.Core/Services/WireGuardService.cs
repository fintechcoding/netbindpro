// Copyright (c) 2024 NetBind Pro Contributors
// Licensed under the MIT License. See LICENSE in the project root for details.

using System.Diagnostics;
using System.Net.Http;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NetBindPro.Core.Models;

namespace NetBindPro.Core.Services;

/// <summary>
/// Manages multiple WireGuard tunnels via the official <c>wireguard.exe</c>
/// tunnel-service mechanism. Each imported <c>.conf</c> can be brought up as a
/// Windows service (<c>WireGuardTunnel$&lt;name&gt;</c>), which creates a wintun
/// adapter named after the tunnel. Those adapters are then discoverable by
/// <see cref="AdapterDiscovery"/> and bindable by the injection engine, so a
/// rule can route a process through a specific tunnel.
/// </summary>
/// <remarks>
/// Requires WireGuard for Windows to be installed (for <c>wireguard.exe</c>).
/// Installing/removing a tunnel service requires administrator rights; the app
/// already runs elevated. A future version may embed <c>tunnel.dll</c> to drop
/// the external dependency.
/// </remarks>
public sealed class WireGuardService
{
    private static readonly string TunnelsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "NetBindPro", "tunnels");

    private static readonly string[] CandidateExePaths =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WireGuard", "wireguard.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "WireGuard", "wireguard.exe"),
    ];

    private readonly ILogger<WireGuardService>? _logger;

    public WireGuardService(ILogger<WireGuardService>? logger = null)
    {
        _logger = logger;
        Directory.CreateDirectory(TunnelsDirectory);
    }

    // ─── Availability ────────────────────────────────────────────────────────

    /// <summary>Full path to <c>wireguard.exe</c>, or <c>null</c> if not installed.</summary>
    public static string? WireGuardExePath => CandidateExePaths.FirstOrDefault(File.Exists);

    /// <summary>Whether WireGuard for Windows appears to be installed.</summary>
    public static bool IsWireGuardInstalled => WireGuardExePath != null;

    /// <summary>Official stable URL that always fetches the latest WireGuard for Windows.</summary>
    private const string WireGuardInstallerUrl =
        "https://download.wireguard.com/windows-client/wireguard-installer.exe";

    /// <summary>
    /// Ensures WireGuard for Windows is installed, downloading and launching the
    /// official installer if it is missing. Returns success once <c>wireguard.exe</c>
    /// is present. The installer requires a UAC prompt.
    /// </summary>
    public async Task<(bool Success, string Message)> EnsureWireGuardInstalledAsync(
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (IsWireGuardInstalled)
            return (true, "WireGuard is already installed.");

        var installerPath = Path.Combine(Path.GetTempPath(), "wireguard-installer.exe");

        try
        {
            progress?.Report("Downloading WireGuard for Windows…");
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
            using (var resp = await http.GetAsync(WireGuardInstallerUrl,
                       HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                resp.EnsureSuccessStatusCode();
                await using var fs = File.Create(installerPath);
                await resp.Content.CopyToAsync(fs, ct).ConfigureAwait(false);
            }

            progress?.Report("Running the WireGuard installer (approve the UAC prompt)…");
            using (var proc = Process.Start(new ProcessStartInfo
                   {
                       FileName        = installerPath,
                       UseShellExecute = true, // triggers UAC elevation for the MSI
                   }))
            {
                if (proc != null)
                    await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "WireGuard download/install failed");
            return (false, $"Download or install failed: {ex.Message}");
        }

        // The installer may take a moment to drop wireguard.exe in place.
        for (var i = 0; i < 10 && !IsWireGuardInstalled; i++)
            await Task.Delay(500, ct).ConfigureAwait(false);

        return IsWireGuardInstalled
            ? (true, "WireGuard installed successfully.")
            : (false, "Installation did not complete. Please install WireGuard for Windows manually.");
    }

    // ─── Import ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Imports a <c>.conf</c> file into the managed tunnels directory and returns a
    /// <see cref="TunnelConfig"/> describing it. Does not start the tunnel.
    /// </summary>
    public TunnelConfig ImportConfig(string sourceConfPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceConfPath);
        if (!File.Exists(sourceConfPath))
            throw new FileNotFoundException("Config file not found.", sourceConfPath);

        var name = SanitizeName(Path.GetFileNameWithoutExtension(sourceConfPath));
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Could not derive a valid tunnel name from the file.");

        var destPath = Path.Combine(TunnelsDirectory, name + ".conf");
        File.Copy(sourceConfPath, destPath, overwrite: true);
        // The tunnels directory lives under %APPDATA% (per-user) so the private key
        // in the config is not world-readable. A future pass can tighten the ACL further.

        var text = File.ReadAllText(destPath);
        var tunnel = new TunnelConfig
        {
            Name       = name,
            ConfigPath = destPath,
            Endpoint   = MatchValue(text, @"Endpoint\s*=\s*(.+)"),
            Address    = MatchValue(text, @"Address\s*=\s*(.+)"),
        };

        _logger?.LogInformation("Imported tunnel {Name} from {Source}", name, sourceConfPath);
        return tunnel;
    }

    // ─── Lifecycle ───────────────────────────────────────────────────────────

    /// <summary>
    /// Brings the tunnel up by installing its tunnel service. Idempotent-ish:
    /// if the service already exists this is a no-op success.
    /// </summary>
    public async Task<(bool Success, string Message)> StartAsync(TunnelConfig tunnel, CancellationToken ct = default)
    {
        if (!IsWireGuardInstalled)
            return (false, "WireGuard for Windows is not installed (wireguard.exe not found).");
        if (!File.Exists(tunnel.ConfigPath))
            return (false, $"Config file missing: {tunnel.ConfigPath}");

        if (GetState(tunnel) == TunnelState.Running)
            return (true, "Already running.");

        var (code, _, err) = await RunWgAsync(["/installtunnelservice", tunnel.ConfigPath], ct).ConfigureAwait(false);
        if (code != 0)
            return (false, string.IsNullOrWhiteSpace(err) ? $"wireguard.exe exited with {code}." : err.Trim());

        _logger?.LogInformation("Started tunnel {Name}", tunnel.Name);
        return (true, "Tunnel started.");
    }

    /// <summary>Brings the tunnel down by removing its tunnel service.</summary>
    public async Task<(bool Success, string Message)> StopAsync(TunnelConfig tunnel, CancellationToken ct = default)
    {
        if (!IsWireGuardInstalled)
            return (false, "WireGuard for Windows is not installed.");

        if (GetState(tunnel) == TunnelState.Stopped)
            return (true, "Already stopped.");

        var (code, _, err) = await RunWgAsync(["/uninstalltunnelservice", tunnel.Name], ct).ConfigureAwait(false);
        if (code != 0)
            return (false, string.IsNullOrWhiteSpace(err) ? $"wireguard.exe exited with {code}." : err.Trim());

        _logger?.LogInformation("Stopped tunnel {Name}", tunnel.Name);
        return (true, "Tunnel stopped.");
    }

    // ─── Status ──────────────────────────────────────────────────────────────

    /// <summary>Current state of the tunnel's Windows service.</summary>
    public TunnelState GetState(TunnelConfig tunnel)
    {
        var serviceName = "WireGuardTunnel$" + tunnel.Name;
        try
        {
            using var sc = new ServiceController(serviceName);
            return sc.Status switch
            {
                ServiceControllerStatus.Running         => TunnelState.Running,
                ServiceControllerStatus.StartPending    => TunnelState.Starting,
                ServiceControllerStatus.ContinuePending => TunnelState.Starting,
                ServiceControllerStatus.Stopped         => TunnelState.Stopped,
                ServiceControllerStatus.StopPending     => TunnelState.Stopped,
                _                                        => TunnelState.Unknown,
            };
        }
        catch (InvalidOperationException)
        {
            // No such service → tunnel is not installed/running.
            return TunnelState.Stopped;
        }
        catch
        {
            return TunnelState.Unknown;
        }
    }

    /// <summary>
    /// Resolves the live adapter for a running tunnel by matching the adapter name
    /// to the tunnel name. Returns <c>null</c> when the tunnel is down.
    /// </summary>
    public static AdapterInfo? ResolveAdapter(string tunnelName)
        => AdapterDiscovery.GetAll(includeDown: true)
            .FirstOrDefault(a =>
                a.Name.Equals(tunnelName, StringComparison.OrdinalIgnoreCase) ||
                a.Description.Contains(tunnelName, StringComparison.OrdinalIgnoreCase));

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static async Task<(int Code, string Out, string Err)> RunWgAsync(string[] args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName               = WireGuardExePath!,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = new Process { StartInfo = psi };
        proc.Start();

        var outTask = proc.StandardOutput.ReadToEndAsync(ct);
        var errTask = proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct).ConfigureAwait(false);

        return (proc.ExitCode, await outTask.ConfigureAwait(false), await errTask.ConfigureAwait(false));
    }

    private static string SanitizeName(string raw)
    {
        // WireGuard adapter/service names must be reasonably simple.
        var cleaned = Regex.Replace(raw, @"[^A-Za-z0-9_=+.-]", "_").Trim('_');
        return cleaned.Length > 32 ? cleaned[..32] : cleaned;
    }

    private static string? MatchValue(string text, string pattern)
    {
        var m = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }
}
