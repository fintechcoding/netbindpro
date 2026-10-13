// Copyright (c) 2024 NetBind Pro Contributors
// Licensed under the MIT License. See LICENSE in the project root for details.

using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using NetBindPro.Core.Models;

namespace NetBindPro.Core.Services;

/// <summary>
/// Result of an egress (public exit IP) check performed through a specific adapter.
/// </summary>
public sealed record EgressResult(
    bool    Success,
    string? PublicIp,
    string? Country,
    string? Error,
    long    ElapsedMs);

/// <summary>
/// Verifies which public IP address a given adapter exits from.
/// <para>
/// This runs inside NetBind Pro itself — there is no need to open a browser or a
/// site like 2ip.com inside the target process. We create our own TCP socket,
/// <b>bind it to the adapter's local IP</b> (exactly like the injection hook does),
/// then query a plain IP-echo endpoint. The response is the adapter/tunnel's public
/// exit IP. If the tunnel is down, the connection simply fails, which itself confirms
/// that traffic would not leak out another route.
/// </para>
/// </summary>
public static class EgressCheckService
{
    // Tiny, text-only "what is my IP" endpoints. Tried in order until one answers.
    private static readonly string[] IpEchoUrls =
    [
        "https://api.ipify.org",
        "https://icanhazip.com",
        "https://ifconfig.me/ip",
    ];

    /// <summary>
    /// Resolves the public exit IP (and best-effort country) reached when traffic is
    /// bound to <paramref name="adapter"/>'s local address.
    /// </summary>
    public static async Task<EgressResult> CheckAsync(AdapterInfo adapter, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        var bindAddr = ResolveBindAddress(adapter);
        if (bindAddr is null)
            return new EgressResult(false, null, null, "Adapter has no usable IP address.", sw.ElapsedMilliseconds);

        using var handler = new SocketsHttpHandler
        {
            // Force every outbound connection to egress from the adapter's local IP.
            ConnectCallback = async (context, cancel) =>
            {
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    socket.Bind(new IPEndPoint(bindAddr, 0));
                    await socket.ConnectAsync(context.DnsEndPoint, cancel).ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
            AutomaticDecompression = System.Net.DecompressionMethods.All,
        };

        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("NetBindPro/2.0 (egress-check)");

        string? ip        = null;
        string? lastError = null;

        foreach (var url in IpEchoUrls)
        {
            try
            {
                var text = (await http.GetStringAsync(url, ct).ConfigureAwait(false)).Trim();
                if (IPAddress.TryParse(text, out _))
                {
                    ip = text;
                    break;
                }
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
            }
        }

        if (ip is null)
            return new EgressResult(false, null, null,
                lastError ?? "No IP-echo endpoint responded (tunnel down?).", sw.ElapsedMilliseconds);

        var country = await TryGetCountryAsync(http, ip, ct).ConfigureAwait(false);
        return new EgressResult(true, ip, country, null, sw.ElapsedMilliseconds);
    }

    private static IPAddress? ResolveBindAddress(AdapterInfo adapter)
    {
        if (!string.IsNullOrWhiteSpace(adapter.IpV4Address) &&
            IPAddress.TryParse(adapter.IpV4Address, out var v4))
            return v4;

        if (!string.IsNullOrWhiteSpace(adapter.IpV6Address) &&
            IPAddress.TryParse(adapter.IpV6Address, out var v6))
            return v6;

        return null;
    }

    private static async Task<string?> TryGetCountryAsync(HttpClient http, string ip, CancellationToken ct)
    {
        try
        {
            // Best-effort, non-fatal. Queried through the same bound handler.
            var json = await http.GetStringAsync(
                $"http://ip-api.com/json/{ip}?fields=country", ct).ConfigureAwait(false);
            var m = Regex.Match(json, "\"country\"\\s*:\\s*\"([^\"]+)\"");
            return m.Success ? m.Groups[1].Value : null;
        }
        catch
        {
            return null;
        }
    }
}
