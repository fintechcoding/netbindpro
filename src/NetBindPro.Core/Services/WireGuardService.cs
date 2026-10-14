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
        var rawText  = File.ReadAllText(sourceConfPath);

        var existingMtu = ParseMtu(rawText);

        // Rewrite [Interface] with `Table = off` so WireGuard does NOT take over the
        // system's default route or install its firewall "kill switch" that blocks all
        // non-tunnel traffic — both of which break per-process routing (the whole PC
        // would go through the tunnel and other apps would lose the internet). We add a
        // high-metric route ourselves on start so only source-bound sockets use it.
        var rewritten = RewriteInterface(rawText, existingMtu);
        File.WriteAllText(destPath, rewritten);
        // The tunnels directory lives under %APPDATA% (per-user) so the private key
        // in the config is not world-readable. A future pass can tighten the ACL further.

        var tunnel = new TunnelConfig
        {
            Name       = name,
            ConfigPath = destPath,
            Endpoint   = MatchValue(rawText, @"Endpoint\s*=\s*(.+)"),
            Address    = MatchValue(rawText, @"Address\s*=\s*(.+)"),
            Mtu        = existingMtu,
        };

        _logger?.LogInformation("Imported tunnel {Name} from {Source} (Table=off applied)", name, sourceConfPath);
        return tunnel;
    }

    /// <summary>
    /// Rewrites the <c>[Interface]</c> section to guarantee <c>Table = off</c> and,
    /// when <paramref name="mtu"/> is set, an <c>MTU</c> line. Existing Table/MTU lines
    /// are replaced. Other sections and keys are preserved verbatim.
    /// </summary>
    private static string RewriteInterface(string confText, int? mtu)
    {
        var lines   = confText.Replace("\r\n", "\n").Split('\n').ToList();
        var output  = new List<string>();
        var inIface = false;
        var injected = false;

        void InjectDirectives()
        {
            output.Add("Table = off");
            if (mtu is > 0) output.Add($"MTU = {mtu}");
            injected = true;
        }

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith('['))
            {
                // Leaving the [Interface] section without having injected (empty section) — inject now.
                if (inIface && !injected) InjectDirectives();
                inIface = trimmed.Equals("[Interface]", StringComparison.OrdinalIgnoreCase);
                output.Add(line);
                if (inIface) { injected = false; InjectDirectives(); }
                continue;
            }

            // Drop any existing Table/MTU lines inside [Interface]; we control them.
            if (inIface && Regex.IsMatch(trimmed, @"^(Table|MTU)\s*=", RegexOptions.IgnoreCase))
                continue;

            output.Add(line);
        }

        if (inIface && !injected) InjectDirectives();
        return string.Join(Environment.NewLine, output);
    }

    private static int? ParseMtu(string confText)
    {
        var v = MatchValue(confText, @"MTU\s*=\s*(\d+)");
        return int.TryParse(v, out var n) && n > 0 ? n : null;
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

        // Wait for the wintun adapter to come up, then add a high-metric default route
        // through it. With Table=off WireGuard adds no routes, so without this a socket
        // bound to the tunnel IP has no path out. The high metric keeps the tunnel from
        // becoming the system default — only source-bound (injected) sockets use it.
        for (var i = 0; i < 12 && ResolveAdapter(tunnel.Name) == null; i++)
            await Task.Delay(500, ct).ConfigureAwait(false);
        await TryAddTunnelRouteAsync(tunnel.Name, ct).ConfigureAwait(false);

        _logger?.LogInformation("Started tunnel {Name}", tunnel.Name);
        return (true, "Tunnel started.");
    }

    /// <summary>
    /// Adds a high-metric default route via the tunnel interface (best-effort) so that
    /// source-bound sockets egress through it while the rest of the system keeps using
    /// its normal default route.
    /// </summary>
    private async Task TryAddTunnelRouteAsync(string tunnelName, CancellationToken ct)
    {
        try
        {
            var nic = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n =>
                    n.Name.Equals(tunnelName, StringComparison.OrdinalIgnoreCase) &&
                    n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up);
            if (nic == null) { _logger?.LogWarning("Route: tunnel adapter {Name} not up", tunnelName); return; }

            int ifIndex;
            try { ifIndex = nic.GetIPProperties().GetIPv4Properties().Index; }
            catch { _logger?.LogWarning("Route: no IPv4 props for {Name}", tunnelName); return; }

            // store=active so it's not persisted; it goes away when the tunnel stops.
            var (c, _, e) = await RunProcessAsync("netsh",
                ["interface", "ipv4", "add", "route", "prefix=0.0.0.0/0",
                 $"interface={ifIndex}", "metric=9000", "store=active"], ct).ConfigureAwait(false);

            if (c != 0 && !e.Contains("already exists", StringComparison.OrdinalIgnoreCase))
                _logger?.LogWarning("Route add failed for {Name}: {Err}", tunnelName, e.Trim());
            else
                _logger?.LogInformation("High-metric route added via tunnel {Name} (if {Index})", tunnelName, ifIndex);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Route add error for {Name}", tunnelName);
        }
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

    /// <summary>
    /// Resolves the adapter a rule should bind to. When the rule targets a managed
    /// tunnel by name, the tunnel's <b>current</b> adapter id is used (so the rule
    /// survives the tunnel being re-created with a new GUID). Falls back to the
    /// rule's stored adapter id/name otherwise, or when the tunnel is down.
    /// </summary>
    public static (string AdapterId, string AdapterName) ResolveRuleAdapter(BindingRule rule)
    {
        if (!string.IsNullOrWhiteSpace(rule.TunnelName))
        {
            var adapter = ResolveAdapter(rule.TunnelName!);
            if (adapter != null)
                return (adapter.Id, adapter.Name);
        }
        return (rule.AdapterId, rule.AdapterName);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static Task<(int Code, string Out, string Err)> RunWgAsync(string[] args, CancellationToken ct)
        => RunProcessAsync(WireGuardExePath!, args, ct);

    private static async Task<(int Code, string Out, string Err)> RunProcessAsync(
        string fileName, string[] args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName               = fileName,
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

    // ─── MTU ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Probes the usable interface MTU for this tunnel by finding the path MTU to its
    /// endpoint (ICMP with the Don't-Fragment bit) and subtracting WireGuard's overhead.
    /// Useful when the tunnel is nested inside another VPN (e.g. OpenVPN), where the
    /// default 1420 is too large and the handshake or traffic silently fails.
    /// Returns a recommended MTU in [1280, 1420]; falls back to 1280 if ICMP is blocked.
    /// </summary>
    public static async Task<(bool Ok, int Mtu, string Message)> ProbeMtuAsync(
        string? endpointHostPort, CancellationToken ct = default)
    {
        const int wgOverhead = 80;   // WG + UDP + IP headroom (IPv6-safe)
        const int floor = 1280, ceiling = 1420;

        var host = ParseEndpointHost(endpointHostPort);
        if (string.IsNullOrWhiteSpace(host))
            return (false, floor, "No endpoint in config; using safe MTU 1280.");

        using var ping = new System.Net.NetworkInformation.Ping();
        var opts = new System.Net.NetworkInformation.PingOptions(64, dontFragment: true);
        var buf  = (int payload) => new byte[payload];

        // Sanity: can we reach it at all with a small DF packet?
        try
        {
            var probe = await ping.SendPingAsync(host, 1500, buf(500), opts).ConfigureAwait(false);
            if (probe.Status != System.Net.NetworkInformation.IPStatus.Success)
                return (false, floor, $"Endpoint doesn't answer ping ({probe.Status}); using safe MTU 1280.");
        }
        catch (Exception ex)
        {
            return (false, floor, $"Ping failed ({ex.Message}); using safe MTU 1280.");
        }

        // Binary-search the largest DF payload that still gets through.
        int lo = 500, hi = ceiling - 28, best = 500;
        while (lo <= hi)
        {
            ct.ThrowIfCancellationRequested();
            var mid = (lo + hi) / 2;
            bool ok;
            try
            {
                var r = await ping.SendPingAsync(host, 1500, buf(mid), opts).ConfigureAwait(false);
                ok = r.Status == System.Net.NetworkInformation.IPStatus.Success;
            }
            catch { ok = false; }

            if (ok) { best = mid; lo = mid + 1; }
            else    { hi = mid - 1; }
        }

        var pathMtu = best + 28;                 // + IP(20) + ICMP(8)
        var ifaceMtu = Math.Clamp(pathMtu - wgOverhead, floor, ceiling);
        return (true, ifaceMtu, $"Path MTU ~{pathMtu}, recommended tunnel MTU {ifaceMtu}.");
    }

    /// <summary>
    /// Writes <paramref name="mtu"/> into the tunnel's stored config (rewriting the
    /// [Interface] MTU line) and updates <paramref name="tunnel"/>. The tunnel must be
    /// restarted for the change to take effect.
    /// </summary>
    public void SetMtu(TunnelConfig tunnel, int? mtu)
    {
        tunnel.Mtu = mtu is > 0 ? mtu : null;
        if (File.Exists(tunnel.ConfigPath))
        {
            var text = File.ReadAllText(tunnel.ConfigPath);
            File.WriteAllText(tunnel.ConfigPath, RewriteInterface(text, tunnel.Mtu));
        }
        _logger?.LogInformation("Set MTU {Mtu} for tunnel {Name}", tunnel.Mtu, tunnel.Name);
    }

    private static string? ParseEndpointHost(string? endpointHostPort)
    {
        if (string.IsNullOrWhiteSpace(endpointHostPort)) return null;
        var ep = endpointHostPort.Trim();
        // [ipv6]:port
        if (ep.StartsWith('['))
        {
            var end = ep.IndexOf(']');
            return end > 1 ? ep[1..end] : null;
        }
        // host:port  (take everything before the last colon)
        var colon = ep.LastIndexOf(':');
        return colon > 0 ? ep[..colon] : ep;
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
