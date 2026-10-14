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
    public string SubText        => Rule.AutoInject ? "⚡ auto-inject" : "manual";
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
        var processes = System.Diagnostics.Process.GetProcessesByName(
            System.IO.Path.GetFileNameWithoutExtension(Rule.ProcessName));

        if (processes.Length == 0)
        {
            MessageBox.Show($"No running process found matching '{Rule.ProcessName}'.",
                "NetBind Pro", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var (adapterId, adapterName) = WireGuardService.ResolveRuleAdapter(Rule);

        foreach (var proc in processes)
        {
            var result = await _injection.InjectAsync(proc.Id, adapterId, adapterName, Rule.KillSwitch);
            if (!result.Success)
                MessageBox.Show($"Injection failed for PID {proc.Id}: {result.Message}",
                    "NetBind Pro", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
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
        // Open edit dialog (reuses AddRuleWindow pre-filled)
        MessageBox.Show("Edit dialog — implement with pre-filled AddRuleWindow.",
            "NetBind Pro", MessageBoxButton.OK, MessageBoxImage.Information);
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
