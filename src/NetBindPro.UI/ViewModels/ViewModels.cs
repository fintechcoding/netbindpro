// Copyright (c) 2024 NetBind Pro Contributors
// Licensed under the MIT License. See LICENSE in the project root for details.

using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using NetBindPro.Core.Models;
using NetBindPro.Core.Services;
using NetBindPro.UI.Services;

namespace NetBindPro.UI.ViewModels;

// ─── MainViewModel ────────────────────────────────────────────────────────────

public partial class MainViewModel : ObservableObject
{
    private readonly ProfileService _profile;

    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private int    _activeRuleCount;
    [ObservableProperty] private int    _activeInjections;

    public MainViewModel(ProfileService profile)
    {
        _profile = profile;
        Refresh();
    }

    [RelayCommand]
    public void Refresh()
    {
        ActiveRuleCount  = _profile.Rules.Count(r => r.IsEnabled);
        StatusText       = $"{ActiveRuleCount} rules active";
    }
}

// ─── Rule display item ────────────────────────────────────────────────────────

public partial class RuleItemVm : ObservableObject
{
    private readonly ProfileService  _profile;
    private readonly InjectionService _injection;

    public BindingRule Rule { get; }

    public string IconText       { get; }
    public string IconBackground { get; }
    public string IconForeground { get; }
    public string SubText        => Rule.AutoInject ? "⚡ auto-binds when opened" : "launch manually (▶)";
    public string AdapterTagBackground { get; }
    public string AdapterTextColor     { get; }
    public string AdapterDotColor      { get; }

    public RuleItemVm(BindingRule rule, ProfileService profile, InjectionService injection)
    {
        Rule       = rule;
        _profile   = profile;
        _injection = injection;

        IconText       = rule.ProcessName.Length >= 2
            ? rule.ProcessName[..2].ToUpperInvariant()
            : rule.ProcessName.ToUpperInvariant();

        var colors = new[] { "#FF6633", "#2563EB", "#16A34A", "#7C3AED", "#D97706", "#0D9488" };
        var colorHex = colors[Math.Abs(rule.ProcessName.GetHashCode()) % colors.Length];
        IconBackground = colorHex + "18";
        IconForeground = colorHex;

        (AdapterTagBackground, AdapterTextColor, AdapterDotColor) = DetectAdapterStyle(rule.AdapterName);
    }

    private static (string bg, string text, string dot) DetectAdapterStyle(string adapterName)
    {
        var lower = adapterName.ToLowerInvariant();
        if (lower.Contains("wireguard") || lower.StartsWith("wg"))
            return ("#120284C7", "#0284C7", "#0284C7");
        if (lower.Contains("tailscale"))
            return ("#122563EB", "#2563EB", "#2563EB");
        if (lower.Contains("vpn") || lower.Contains("tun") || lower.Contains("tap"))
            return ("#127C3AED", "#7C3AED", "#7C3AED");
        return ("#12555555", "#555555", "#999999");
    }

    [RelayCommand]
    private async Task InjectAsync()
    {
        var (adapterId, adapterName) = WireGuardService.ResolveRuleAdapter(Rule);

        // IMPORTANT: a running browser (Chrome/Firefox/Edge) CANNOT be bound after the
        // fact — their process mitigations block DLL injection into an already-running
        // process (EasyHook: "didn't raise the remote event"). The reliable way is to
        // LAUNCH the app through the Hook so the binding is installed at startup
        // (suspended-create + inject). So this command launches the app bound.
        var exePath = ResolveExePath();
        if (exePath == null)
        {
            MessageBox.Show(
                $"Couldn't find the program for '{Rule.ProcessName}'. Edit the rule and set its full " +
                "path (Browse to the .exe), then try again.",
                "NetBind Pro", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Browsers hand a new launch off to an already-running instance (which is NOT
        // bound), so clicking Launch while Chrome is open would do nothing — the page
        // still shows your real IP. To guarantee an independent, actually-bound process,
        // browser rules launch into their OWN data directory (a separate "tunnel profile",
        // kept apart from your normal browsing). Non-browser apps get no special args, so
        // we warn if one is already running (it can't be bound after the fact).
        var (launchArgs, isolatedBrowser) = BuildLaunchArgs();

        if (!isolatedBrowser)
        {
            var baseName = System.IO.Path.GetFileNameWithoutExtension(Rule.ProcessName);
            if (System.Diagnostics.Process.GetProcessesByName(baseName).Length > 0)
            {
                var go = MessageBox.Show(
                    $"{Rule.ProcessName} is already running.\n\n" +
                    "A running app can't be bound after it has started — close ALL of its windows " +
                    "first, then launch it bound from here.\n\nLaunch a new bound instance anyway?",
                    "NetBind Pro", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (go != MessageBoxResult.Yes) return;
            }
        }

        // delayMs 0 → the Hook uses suspended-create + inject (the only path that reliably
        // binds a hardened browser), and its CreateProcess hook carries the binding into
        // the child processes (the network/socket process where traffic actually flows).
        var result = await _injection.ExecuteAndInjectAsync(
            exePath, adapterId, adapterName, arguments: launchArgs, delayMs: 0, killSwitch: Rule.KillSwitch);

        MessageBox.Show(
            result.Success
                ? $"Launched '{Rule.ProcessName}' bound to {adapterName}." +
                  (isolatedBrowser ? "\n\nIt opens in a separate \"tunnel\" profile so it is always actually routed." : "")
                : $"Couldn't launch '{Rule.ProcessName}' bound to {adapterName}: {result.Message}",
            "NetBind Pro", MessageBoxButton.OK,
            result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    /// <summary>
    /// Builds launch arguments for the rule. For a browser, returns args that open an
    /// isolated per-rule data directory so the launch can't hand off to an already-running
    /// (un-bound) instance. Returns (args, isolatedBrowser).
    /// </summary>
    private (string args, bool isolatedBrowser) BuildLaunchArgs()
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(Rule.ProcessName).ToLowerInvariant();
        var dir  = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetBindPro", "profiles", name);
        try { System.IO.Directory.CreateDirectory(dir); } catch { /* best-effort */ }

        // Chromium family: a distinct --user-data-dir forces a brand-new browser process.
        if (name is "chrome" or "msedge" or "brave" or "chromium" or "opera" or "vivaldi" or "thorium")
            return ($"--user-data-dir=\"{dir}\" --no-first-run --no-default-browser-check", true);

        // Firefox: a dedicated profile + --no-remote starts an independent instance.
        // (Note: Firefox's launcher still prevents the bind from sticking — see README.)
        if (name is "firefox")
            return ($"-no-remote -profile \"{dir}\"", true);

        return (string.Empty, false);
    }

    /// <summary>Resolves the executable to launch: the rule's stored path, else a running
    /// instance's image path, else null (caller asks the user to set it).</summary>
    private string? ResolveExePath()
    {
        if (!string.IsNullOrWhiteSpace(Rule.ProcessPath) && System.IO.File.Exists(Rule.ProcessPath))
            return Rule.ProcessPath;

        try
        {
            var baseName = System.IO.Path.GetFileNameWithoutExtension(Rule.ProcessName);
            foreach (var p in System.Diagnostics.Process.GetProcessesByName(baseName))
            {
                try { var f = p.MainModule?.FileName; if (!string.IsNullOrEmpty(f)) return f; }
                catch { /* protected / cross-arch */ }
            }
        }
        catch { /* ignore */ }
        return null;
    }

    [RelayCommand]
    private void Verify()
    {
        // Prove the rule works: every outbound connection of the running process must
        // originate from the chosen adapter's IP. Anything on another IP is leaking.
        var (adapterId, adapterName) = WireGuardService.ResolveRuleAdapter(Rule);
        var adapter = AdapterDiscovery.GetAll(includeDown: true).FirstOrDefault(a =>
            a.Id.Trim('{', '}').Equals(adapterId.Trim('{', '}'), StringComparison.OrdinalIgnoreCase));
        var adapterIp = adapter?.IpV4Address;

        if (string.IsNullOrWhiteSpace(adapterIp))
        {
            MessageBox.Show(
                $"The adapter for this rule ({adapterName}) isn't up or has no IPv4 address. " +
                "Start its tunnel first.", "Verify", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var baseName = System.IO.Path.GetFileNameWithoutExtension(Rule.ProcessName);
        var procs = System.Diagnostics.Process.GetProcessesByName(baseName);
        if (procs.Length == 0)
        {
            MessageBox.Show(
                $"'{Rule.ProcessName}' isn't running. Launch it bound (▶) first, then Verify.",
                "Verify", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var onAdapter = 0;
        var leaking = new List<string>();
        foreach (var p in procs)
        {
            foreach (var addr in ConnectionInspector.GetOutboundLocalAddresses(p.Id))
            {
                if (addr == adapterIp) onAdapter++;
                else if (!leaking.Contains(addr)) leaking.Add(addr);
            }
        }

        string msg; MessageBoxImage icon;
        if (onAdapter > 0 && leaking.Count == 0)
        {
            msg = $"✓ Tunneled correctly.\n\nAll {onAdapter} active connection(s) of '{Rule.ProcessName}' " +
                  $"go out through {adapterName} ({adapterIp}).";
            icon = MessageBoxImage.Information;
        }
        else if (onAdapter > 0)
        {
            msg = $"⚠ Partly tunneled.\n\n{onAdapter} connection(s) go through {adapterName} ({adapterIp}), " +
                  $"but some are leaking on: {string.Join(", ", leaking)}.";
            icon = MessageBoxImage.Warning;
        }
        else if (leaking.Count > 0)
        {
            msg = $"✗ NOT tunneled.\n\n'{Rule.ProcessName}' is connecting from {string.Join(", ", leaking)}, " +
                  $"not {adapterName} ({adapterIp}). Close it and launch it bound (▶) from here.";
            icon = MessageBoxImage.Error;
        }
        else
        {
            msg = $"'{Rule.ProcessName}' is running but has no active outbound connections yet.\n\n" +
                  "Make it connect to something, then Verify again.";
            icon = MessageBoxImage.Information;
        }
        MessageBox.Show(msg, "Verify", MessageBoxButton.OK, icon);
    }

    [RelayCommand]
    private void Toggle()
    {
        Rule.IsEnabled = !Rule.IsEnabled;
        _profile.UpdateRule(Rule);
        OnPropertyChanged(nameof(Rule));
    }

    [RelayCommand]
    private void Delete()
    {
        var confirm = MessageBox.Show(
            $"Delete rule for '{Rule.ProcessName}'?",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm == MessageBoxResult.Yes)
            _profile.RemoveRule(Rule.Id);
    }

    [RelayCommand]
    private void Edit()
    {
        // Reuse AddRuleWindow in edit mode, pre-filled from this rule.
        var dialog = App.GetService<Views.AddRuleWindow>();
        dialog.Owner = Application.Current?.MainWindow;
        dialog.LoadForEdit(Rule);
        dialog.ShowDialog();
        // The rule list refreshes via ProfileService.RulesChanged (UpdateRule raises it).
    }
}

// ─── RulesViewModel ──────────────────────────────────────────────────────────

public partial class RulesViewModel : ObservableObject
{
    private readonly ProfileService   _profile;
    private readonly InjectionService _injection;

    public ObservableCollection<RuleItemVm> Rules { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Rules))]
    private string _filterText = string.Empty;

    public RulesViewModel(ProfileService profile, InjectionService injection)
    {
        _profile   = profile;
        _injection = injection;

        // Keep the list in sync when rules are added/removed/updated anywhere
        // (e.g. a row's Delete command), marshalling back to the UI thread.
        _profile.RulesChanged += () =>
            Application.Current?.Dispatcher.Invoke(Refresh);

        Refresh();
    }

    // Re-filter immediately as the user types in the search box.
    partial void OnFilterTextChanged(string value) => Refresh();

    [RelayCommand]
    public void Refresh()
    {
        Rules.Clear();
        var query = _profile.Rules.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(FilterText))
            query = query.Where(r =>
                r.ProcessName.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ||
                r.AdapterName.Contains(FilterText, StringComparison.OrdinalIgnoreCase));

        foreach (var rule in query)
            Rules.Add(new RuleItemVm(rule, _profile, _injection));
    }
}

// ─── AdaptersViewModel ────────────────────────────────────────────────────────

public partial class AdaptersViewModel : ObservableObject
{
    public ObservableCollection<AdapterInfo> Adapters { get; } = [];

    public AdaptersViewModel() => Refresh();

    [RelayCommand]
    public void Refresh()
    {
        Adapters.Clear();
        foreach (var a in AdapterDiscovery.GetAll())
            Adapters.Add(a);
    }
}

// ─── SettingsViewModel ───────────────────────────────────────────────────────

public partial class SettingsViewModel : ObservableObject
{
    private readonly ProfileService _profile;

    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private bool _minimizeToTray;
    [ObservableProperty] private bool _showAdapterInTitle;
    [ObservableProperty] private bool _showNotifications;
    [ObservableProperty] private bool _debugMode;
    [ObservableProperty] private bool _monitorNewProcesses;
    [ObservableProperty] private int  _injectionDelayMs;

    public SettingsViewModel(ProfileService profile)
    {
        _profile = profile;
        Load();
    }

    private void Load()
    {
        var s             = _profile.Settings;
        StartWithWindows  = s.StartWithWindows;
        MinimizeToTray    = s.MinimizeToTray;
        ShowAdapterInTitle = s.ShowAdapterInWindowTitle;
        ShowNotifications = s.ShowNotifications;
        DebugMode         = s.DebugMode;
        MonitorNewProcesses = s.MonitorNewProcesses;
        InjectionDelayMs  = s.InjectionDelayMs;
    }

    [RelayCommand]
    public void Save()
    {
        _profile.SaveSettings(new AppSettings
        {
            StartWithWindows      = StartWithWindows,
            MinimizeToTray        = MinimizeToTray,
            ShowAdapterInWindowTitle = ShowAdapterInTitle,
            ShowNotifications     = ShowNotifications,
            DebugMode             = DebugMode,
            MonitorNewProcesses   = MonitorNewProcesses,
            InjectionDelayMs      = InjectionDelayMs
        });

        MessageBox.Show("Settings saved.", "NetBind Pro",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }
}

// ─── LogViewModel ─────────────────────────────────────────────────────────────

public partial class LogViewModel : ObservableObject
{
    public ObservableCollection<LogEntry> Entries { get; } = [];

    public void Append(LogLevel level, string message)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            Entries.Insert(0, new LogEntry(level, message));
            while (Entries.Count > 500)
                Entries.RemoveAt(Entries.Count - 1);
        });
    }

    [RelayCommand]
    public void Clear() => Entries.Clear();
}

public record LogEntry(LogLevel Level, string Message)
{
    public string Timestamp { get; } = DateTime.Now.ToString("HH:mm:ss");
    public string LevelText => Level switch
    {
        LogLevel.Information => "INFO",
        LogLevel.Warning     => "WARN",
        LogLevel.Error       => "ERR ",
        LogLevel.Debug       => "DBG ",
        _                    => "    "
    };
    public string Color => Level switch
    {
        LogLevel.Information => "#16A34A",
        LogLevel.Warning     => "#D97706",
        LogLevel.Error       => "#DC2626",
        LogLevel.Debug       => "#999999",
        _                    => "#555555"
    };
}
