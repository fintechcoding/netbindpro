// Copyright (c) 2024 NetBind Pro Contributors
// Licensed under the MIT License. See LICENSE in the project root for details.

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using NetBindPro.Core.Models;

namespace NetBindPro.Core.Services;

/// <summary>
/// Manages loading and saving the application profile (rules + settings)
/// to <c>%APPDATA%\NetBindPro\profile.json</c>.
/// All writes are atomic (write-to-temp, then rename) to avoid corruption.
/// </summary>
public sealed class ProfileService
{
    // ─── Constants ──────────────────────────────────────────────────────────

    private static readonly string ConfigDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "NetBindPro");

    private static readonly string ProfilePath = Path.Combine(ConfigDirectory, "profile.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    // ─── Fields ─────────────────────────────────────────────────────────────

    private readonly ILogger<ProfileService>? _logger;
    private AppProfile _profile = new();
    private readonly object _lock = new();

    /// <summary>Raised after the rules collection changes (add/update/remove).</summary>
    public event Action? RulesChanged;

    private void OnRulesChanged() => RulesChanged?.Invoke();

    // ─── Constructor ────────────────────────────────────────────────────────

    public ProfileService(ILogger<ProfileService>? logger = null)
    {
        _logger = logger;
        Load();
    }

    // ─── Public API ─────────────────────────────────────────────────────────

    /// <summary>Current loaded profile. Never null.</summary>
    public AppProfile Profile
    {
        get { lock (_lock) return _profile; }
    }

    /// <summary>Shortcut to the rules collection.</summary>
    public IReadOnlyList<BindingRule> Rules
    {
        get { lock (_lock) return _profile.Rules.AsReadOnly(); }
    }

    /// <summary>Shortcut to the settings object.</summary>
    public AppSettings Settings
    {
        get { lock (_lock) return _profile.Settings; }
    }

    /// <summary>Shortcut to the managed WireGuard tunnels collection.</summary>
    public IReadOnlyList<TunnelConfig> Tunnels
    {
        get { lock (_lock) return _profile.Tunnels.AsReadOnly(); }
    }

    /// <summary>Adds (or replaces by name) a managed tunnel and persists.</summary>
    public TunnelConfig AddTunnel(TunnelConfig tunnel)
    {
        ArgumentNullException.ThrowIfNull(tunnel);
        ArgumentException.ThrowIfNullOrWhiteSpace(tunnel.Name);

        lock (_lock)
        {
            _profile.Tunnels.RemoveAll(t => t.Name.Equals(tunnel.Name, StringComparison.OrdinalIgnoreCase));
            _profile.Tunnels.Add(tunnel);
        }

        Save();
        _logger?.LogInformation("Tunnel added: {Name}", tunnel.Name);
        return tunnel;
    }

    /// <summary>Updates an existing tunnel in-place and persists.</summary>
    public void UpdateTunnel(TunnelConfig updated)
    {
        ArgumentNullException.ThrowIfNull(updated);

        lock (_lock)
        {
            var idx = _profile.Tunnels.FindIndex(t => t.Id == updated.Id);
            if (idx < 0) return;
            _profile.Tunnels[idx] = updated;
        }

        Save();
    }

    /// <summary>Removes a tunnel by ID and persists.</summary>
    public void RemoveTunnel(string tunnelId)
    {
        lock (_lock)
        {
            _profile.Tunnels.RemoveAll(t => t.Id == tunnelId);
        }

        Save();
    }

    /// <summary>
    /// Adds a new binding rule and immediately persists the profile.
    /// </summary>
    public BindingRule AddRule(string processName, string adapterId, string adapterName,
        string? processPath = null, string? notes = null, string? tunnelName = null,
        bool killSwitch = true, bool autoInject = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processName);
        ArgumentException.ThrowIfNullOrWhiteSpace(adapterId);

        var rule = new BindingRule
        {
            ProcessName = Path.GetFileName(processName).ToLowerInvariant(),
            ProcessPath = processPath,
            AdapterId = adapterId,
            AdapterName = adapterName,
            TunnelName = tunnelName,
            KillSwitch = killSwitch,
            AutoInject = autoInject,
            Notes = notes
        };

        lock (_lock)
        {
            _profile.Rules.Add(rule);
        }

        Save();
        OnRulesChanged();
        _logger?.LogInformation("Rule added: {ProcessName} → {AdapterName}", rule.ProcessName, adapterName);
        return rule;
    }

    /// <summary>
    /// Updates an existing rule in-place and persists.
    /// </summary>
    public void UpdateRule(BindingRule updated)
    {
        ArgumentNullException.ThrowIfNull(updated);

        lock (_lock)
        {
            var idx = _profile.Rules.FindIndex(r => r.Id == updated.Id);
            if (idx < 0) return;
            updated.ModifiedAt = DateTime.UtcNow;
            _profile.Rules[idx] = updated;
        }

        Save();
        OnRulesChanged();
    }

    /// <summary>
    /// Removes a rule by ID and persists.
    /// </summary>
    public void RemoveRule(string ruleId)
    {
        lock (_lock)
        {
            _profile.Rules.RemoveAll(r => r.Id == ruleId);
        }

        Save();
        OnRulesChanged();
    }

    /// <summary>
    /// Updates the settings object and persists.
    /// </summary>
    public void SaveSettings(AppSettings settings)
    {
        lock (_lock)
        {
            _profile.Settings = settings;
        }
        Save();
    }

    /// <summary>
    /// Finds the best matching enabled rule for a given process name/path.
    /// Path matching takes precedence over name-only matching.
    /// </summary>
    public BindingRule? FindRuleForProcess(string processName, string? processPath = null)
    {
        var name = Path.GetFileName(processName ?? string.Empty).ToLowerInvariant();

        lock (_lock)
        {
            // Prefer full-path match
            if (!string.IsNullOrEmpty(processPath))
            {
                var pathMatch = _profile.Rules.FirstOrDefault(r =>
                    r.IsEnabled &&
                    !string.IsNullOrEmpty(r.ProcessPath) &&
                    r.ProcessPath.Equals(processPath, StringComparison.OrdinalIgnoreCase));

                if (pathMatch != null) return pathMatch;
            }

            // Fall back to name match
            return _profile.Rules.FirstOrDefault(r =>
                r.IsEnabled &&
                r.ProcessName.Equals(name, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Re-reads the profile from disk, replacing the in-memory copy, and notifies the UI.
    /// Used after an external change to profile.json (e.g. Import) so a later Save() does
    /// not overwrite the imported file with stale in-memory data.
    /// </summary>
    public void Reload()
    {
        Load();
        OnRulesChanged();
    }

    // ─── Private ────────────────────────────────────────────────────────────

    private void Load()
    {
        try
        {
            if (!File.Exists(ProfilePath))
            {
                _logger?.LogInformation("No profile found at {Path}, starting fresh", ProfilePath);
                return;
            }

            var json = File.ReadAllText(ProfilePath);
            var loaded = JsonSerializer.Deserialize<AppProfile>(json, JsonOptions);

            if (loaded != null)
            {
                lock (_lock) { _profile = loaded; }
                _logger?.LogInformation("Profile loaded: {Count} rules", loaded.Rules.Count);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load profile from {Path}, using defaults", ProfilePath);
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDirectory);

            string json;
            lock (_lock)
            {
                json = JsonSerializer.Serialize(_profile, JsonOptions);
            }

            // Atomic write: write to temp file first, then replace
            var tempPath = ProfilePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, ProfilePath, overwrite: true);

            _logger?.LogDebug("Profile saved to {Path}", ProfilePath);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to save profile to {Path}", ProfilePath);
        }
    }
}
