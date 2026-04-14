// Copyright (c) 2024 NetBind Pro Contributors
// Licensed under the MIT License. See LICENSE in the project root for details.

using System.Text.Json.Serialization;

namespace NetBindPro.Core.Models;

/// <summary>
/// Represents a single binding rule that maps a process to a network adapter.
/// </summary>
public sealed class BindingRule
{
    /// <summary>Unique identifier for this rule.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Process executable name, e.g. "firefox.exe". Case-insensitive.</summary>
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>Optional full path to the process executable for more precise matching.</summary>
    public string? ProcessPath { get; set; }

    /// <summary>The GUID of the target network adapter (from NetworkInterface.Id).</summary>
    public string AdapterId { get; set; } = string.Empty;

    /// <summary>Human-readable name of the adapter, stored for display even if adapter goes offline.</summary>
    public string AdapterName { get; set; } = string.Empty;

    /// <summary>Whether this rule is currently active.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Whether to automatically inject when the process starts (requires process monitor).</summary>
    public bool AutoInject { get; set; } = true;

    /// <summary>Notes or description for this rule.</summary>
    public string? Notes { get; set; }

    /// <summary>UTC timestamp when this rule was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>UTC timestamp of last modification.</summary>
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Application-wide settings persisted to disk.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Launch NetBind Pro when Windows starts.</summary>
    public bool StartWithWindows { get; set; } = false;

    /// <summary>Minimize to system tray instead of closing.</summary>
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>Append adapter info to bound window title bar.</summary>
    public bool ShowAdapterInWindowTitle { get; set; } = true;

    /// <summary>Show balloon/toast notifications on injection events.</summary>
    public bool ShowNotifications { get; set; } = true;

    /// <summary>Write verbose debug logs to %TEMP%.</summary>
    public bool DebugMode { get; set; } = false;

    /// <summary>Milliseconds to wait before injecting into a newly started process.</summary>
    public int InjectionDelayMs { get; set; } = 500;

    /// <summary>Whether to monitor newly started processes and auto-inject based on rules.</summary>
    public bool MonitorNewProcesses { get; set; } = true;
}

/// <summary>
/// Root object for the persisted profile (rules + settings).
/// </summary>
public sealed class AppProfile
{
    public string SchemaVersion { get; set; } = "2.0";
    public List<BindingRule> Rules { get; set; } = [];
    public AppSettings Settings { get; set; } = new();
}

/// <summary>
/// Describes a network adapter discovered on the system.
/// </summary>
public sealed class AdapterInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public AdapterKind Kind { get; set; }
    public string? IpV4Address { get; set; }
    public string? IpV6Address { get; set; }
    public bool IsUp { get; set; }
    public bool SupportsMulticast { get; set; }

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Description) ? Name : $"{Name} — {Description}";

    [JsonIgnore]
    public bool IsVpnLike => Kind is AdapterKind.WireGuard or AdapterKind.Tailscale or AdapterKind.Vpn;
}

/// <summary>
/// Classification of network adapter type.
/// </summary>
public enum AdapterKind
{
    Unknown,
    Ethernet,
    WiFi,
    WireGuard,
    Tailscale,
    Vpn,
    Tunnel,
    Loopback,
    Other
}

/// <summary>
/// Result of an injection attempt.
/// </summary>
public sealed class InjectionResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public int ProcessId { get; init; }
    public string ProcessName { get; init; } = string.Empty;
    public string AdapterName { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    public static InjectionResult Ok(int pid, string processName, string adapterName) => new()
    {
        Success = true,
        ProcessId = pid,
        ProcessName = processName,
        AdapterName = adapterName,
        Message = $"Successfully bound {processName} (PID {pid}) to {adapterName}"
    };

    public static InjectionResult Fail(int pid, string processName, string reason) => new()
    {
        Success = false,
        ProcessId = pid,
        ProcessName = processName,
        Message = reason
    };
}
