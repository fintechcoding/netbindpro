// Copyright (c) 2024 NetBind Pro Contributors
// Licensed under the MIT License. See LICENSE in the project root for details.

using System.Net.NetworkInformation;
using System.Net.Sockets;
using NetBindPro.Core.Models;

namespace NetBindPro.Core.Services;

/// <summary>
/// Discovers all network adapters on the system, including WireGuard tunnels,
/// Tailscale interfaces, and VPN adapters that other tools often miss because
/// they incorrectly filter on <see cref="NetworkInterface.SupportsMulticast"/>.
/// </summary>
public static class AdapterDiscovery
{
    // Keywords used to classify adapter type from name/description
    private static readonly string[] WireGuardKeywords = ["wireguard", "wg"];
    private static readonly string[] TailscaleKeywords = ["tailscale"];
    private static readonly string[] VpnKeywords =
    [
        "vpn", "tun", "tap", "openvpn", "nordvpn", "expressvpn",
        "protonvpn", "mullvad", "zerotier", "cisco", "anyconnect",
        "pulse secure", "fortinet", "globalprotect", "softether"
    ];

    /// <summary>
    /// Returns all non-loopback network adapters. When <paramref name="includeDown"/>
    /// is <c>false</c> (default), only adapters with <see cref="OperationalStatus.Up"/> are returned.
    /// </summary>
    public static IReadOnlyList<AdapterInfo> GetAll(bool includeDown = false)
    {
        var result = new List<AdapterInfo>();

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            if (!includeDown && nic.OperationalStatus != OperationalStatus.Up)
                continue;

            result.Add(BuildInfo(nic));
        }

        // Sort: WireGuard/Tailscale first, then other VPNs, then by name
        return result
            .OrderByDescending(a => a.Kind is AdapterKind.WireGuard or AdapterKind.Tailscale)
            .ThenByDescending(a => a.IsVpnLike)
            .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Returns a single adapter by its GUID, or <c>null</c> if not found.
    /// Searches both up and down adapters.
    /// </summary>
    public static AdapterInfo? GetById(string adapterId)
        => GetAll(includeDown: true)
            .FirstOrDefault(a => a.Id.Equals(adapterId, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Returns <c>true</c> if the adapter with <paramref name="adapterId"/> is currently up.
    /// </summary>
    public static bool IsAdapterUp(string adapterId)
    {
        var nic = NetworkInterface.GetAllNetworkInterfaces()
            .FirstOrDefault(n => n.Id.Equals(adapterId, StringComparison.OrdinalIgnoreCase));
        return nic?.OperationalStatus == OperationalStatus.Up;
    }

    // ─── Private helpers ────────────────────────────────────────────────────

    private static AdapterInfo BuildInfo(NetworkInterface nic)
    {
        var name = nic.Name;
        var desc = nic.Description ?? string.Empty;
        var combined = $"{name} {desc}".ToLowerInvariant();

        var kind = ClassifyKind(nic, combined);
        var props = TryGetIpProperties(nic);

        return new AdapterInfo
        {
            Id = nic.Id,
            Name = name,
            Description = desc,
            Kind = kind,
            IsUp = nic.OperationalStatus == OperationalStatus.Up,
            SupportsMulticast = nic.SupportsMulticast,
            IpV4Address = props?.UnicastAddresses
                .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                ?.Address.ToString(),
            IpV6Address = props?.UnicastAddresses
                .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6
                                  && !a.Address.IsIPv6LinkLocal)
                ?.Address.ToString()
        };
    }

    private static AdapterKind ClassifyKind(NetworkInterface nic, string combined)
    {
        if (WireGuardKeywords.Any(k => combined.Contains(k)))
            return AdapterKind.WireGuard;

        if (TailscaleKeywords.Any(k => combined.Contains(k)))
            return AdapterKind.Tailscale;

        if (VpnKeywords.Any(k => combined.Contains(k))
            || nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel
            || nic.NetworkInterfaceType == NetworkInterfaceType.Ppp)
            return AdapterKind.Vpn;

        return nic.NetworkInterfaceType switch
        {
            NetworkInterfaceType.Ethernet        => AdapterKind.Ethernet,
            NetworkInterfaceType.Wireless80211   => AdapterKind.WiFi,
            NetworkInterfaceType.Loopback        => AdapterKind.Loopback,
            _                                    => AdapterKind.Other
        };
    }

    private static IPInterfaceProperties? TryGetIpProperties(NetworkInterface nic)
    {
        try { return nic.GetIPProperties(); }
        catch { return null; }
    }
}
