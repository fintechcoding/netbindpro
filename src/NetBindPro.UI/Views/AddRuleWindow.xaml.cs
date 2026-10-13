// Copyright (c) 2024 NetBind Pro Contributors
// Licensed under the MIT License. See LICENSE in the project root for details.

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
using NetBindPro.Core.Models;
using NetBindPro.Core.Services;

namespace NetBindPro.UI.Views;

public partial class AddRuleWindow : Window
{
    private readonly ProfileService _profile;
    private AdapterInfo?            _selectedAdapter;
    private List<AdapterItemVm>     _adapterVms = [];

    private List<ProcessPickVm> _allProcesses = [];
    private ICollectionView?    _processView;
    private string?             _browsedPath;

    public AddRuleWindow(ProfileService profile)
    {
        _profile = profile;
        InitializeComponent();
        LoadAdapters();
        LoadProcesses();
    }

    // ─── Adapters ───────────────────────────────────────────────────────────

    private void LoadAdapters()
    {
        var adapters = AdapterDiscovery.GetAll()
            // Surface VPN / tunnel adapters first — that's the common case here.
            .OrderByDescending(a => a.IsVpnLike)
            .ThenBy(a => a.Name)
            .ToList();

        _adapterVms = adapters.Select(a => new AdapterItemVm(a)).ToList();
        AdapterList.ItemsSource = _adapterVms;
    }

    private void AdapterItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not AdapterItemVm vm) return;

        foreach (var item in _adapterVms) item.IsSelected = false;
        vm.IsSelected   = true;
        _selectedAdapter = vm.Adapter;
    }

    // ─── Running process picker ─────────────────────────────────────────────

    private void LoadProcesses()
    {
        _allProcesses = EnumerateProcesses();
        _processView  = CollectionViewSource.GetDefaultView(_allProcesses);
        _processView.Filter = FilterProcess;
        ProcessPickList.ItemsSource = _processView;
    }

    private static List<ProcessPickVm> EnumerateProcesses()
    {
        return System.Diagnostics.Process.GetProcesses()
            .Select(p =>
            {
                string? path = null;
                try { path = p.MainModule?.FileName; } catch { /* protected / cross-arch */ }
                return (Name: p.ProcessName, Path: path);
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ProcessPickVm(
                name:  g.Key + ".exe",
                path:  g.Select(x => x.Path).FirstOrDefault(p => !string.IsNullOrEmpty(p)),
                count: g.Count()))
            .OrderBy(vm => vm.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private bool FilterProcess(object obj)
    {
        var term = SearchBox.Text;
        if (string.IsNullOrWhiteSpace(term)) return true;
        return obj is ProcessPickVm vm &&
               vm.Name.Contains(term.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchPlaceholder.Visibility =
            string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;

        // Typing invalidates a previously browsed path unless the text still matches it.
        if (_browsedPath != null &&
            !string.Equals(SearchBox.Text.Trim(), System.IO.Path.GetFileName(_browsedPath),
                StringComparison.OrdinalIgnoreCase))
        {
            _browsedPath = null;
        }

        _processView?.Refresh();
    }

    private void ProcessPickList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProcessPickList.SelectedItem is ProcessPickVm vm)
            _browsedPath = null; // list selection is now the source of truth
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Executables (*.exe)|*.exe|All files (*.*)|*.*",
            Title  = "Select Process Executable"
        };
        if (dlg.ShowDialog() != true) return;

        ProcessPickList.SelectedItem = null;
        _browsedPath   = dlg.FileName;
        SearchBox.Text = System.IO.Path.GetFileName(dlg.FileName);
    }

    // ─── Save ───────────────────────────────────────────────────────────────

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        string  processName;
        string? processPath;

        if (ProcessPickList.SelectedItem is ProcessPickVm selected)
        {
            processName = selected.Name;
            processPath = selected.Path;
        }
        else if (!string.IsNullOrWhiteSpace(SearchBox.Text))
        {
            processName = SearchBox.Text.Trim();
            if (!processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                processName += ".exe";
            processPath = _browsedPath;
        }
        else
        {
            MessageBox.Show("Please pick a running process or type an .exe name.",
                "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_selectedAdapter == null)
        {
            MessageBox.Show("Please select a network adapter.",
                "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _profile.AddRule(
            processName:  processName,
            adapterId:    _selectedAdapter.Id,
            adapterName:  _selectedAdapter.Name,
            processPath:  processPath,
            notes:        string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim()
        );

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

// ─── Running process pick item ──────────────────────────────────────────────

internal sealed class ProcessPickVm
{
    public string  Name { get; }
    public string? Path { get; }
    public int     Count { get; }

    public string Initial         { get; }
    public string IconBackground  { get; }
    public string IconForeground  { get; }

    public string PathDisplay     => string.IsNullOrEmpty(Path) ? "path unavailable" : Path!;
    public string CountText       => $"×{Count}";
    public string CountVisibility => Count > 1 ? "Visible" : "Collapsed";

    public ProcessPickVm(string name, string? path, int count)
    {
        Name  = name;
        Path  = path;
        Count = count;
        Initial = name.Length > 0 ? name[..1].ToUpperInvariant() : "?";

        var colors = new[] { "#FF6633", "#2563EB", "#16A34A", "#7C3AED", "#D97706", "#0D9488" };
        var hex = colors[Math.Abs(name.GetHashCode()) % colors.Length];
        IconBackground = hex + "18";
        IconForeground = hex;
    }
}

// ─── Adapter item view model ────────────────────────────────────────────────

internal class AdapterItemVm : INotifyPropertyChanged
{
    private bool _isSelected;

    public AdapterInfo Adapter    { get; }
    public string      KindLabel  { get; }
    public string      DotColor   { get; }
    public string      BadgeBackground { get; }
    public string      BadgeText  { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            _isSelected = value;
            OnPropertyChanged(nameof(IsSelected));
            OnPropertyChanged(nameof(IsSelectedVisibility));
            OnPropertyChanged(nameof(Background));
            OnPropertyChanged(nameof(BorderBrush));
        }
    }

    public string IsSelectedVisibility => _isSelected ? "Visible" : "Collapsed";
    public string Background => _isSelected ? "#12FF6633" : "Transparent";
    public string BorderBrush => _isSelected ? "#FF6633" : "#E0E0E0";

    public AdapterItemVm(AdapterInfo adapter)
    {
        Adapter = adapter;
        (KindLabel, DotColor, BadgeBackground, BadgeText) = adapter.Kind switch
        {
            AdapterKind.WireGuard => ("WireGuard", "#0284C7", "#120284C7", "#0284C7"),
            AdapterKind.Tailscale => ("Tailscale",  "#2563EB", "#122563EB", "#2563EB"),
            AdapterKind.Vpn       => ("VPN",         "#7C3AED", "#127C3AED", "#7C3AED"),
            AdapterKind.WiFi      => ("Wi-Fi",       "#0D9488", "#120D9488", "#0D9488"),
            AdapterKind.Ethernet  => ("Ethernet",    "#555555", "#12555555", "#555555"),
            _                     => ("Other",       "#555555", "#12555555", "#555555")
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new(name));
}
