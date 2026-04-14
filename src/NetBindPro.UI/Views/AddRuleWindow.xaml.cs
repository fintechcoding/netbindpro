// Copyright (c) 2024 NetBind Pro Contributors
// Licensed under the MIT License. See LICENSE in the project root for details.

using System.Windows;
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

    public AddRuleWindow(ProfileService profile)
    {
        _profile = profile;
        InitializeComponent();
        LoadAdapters();
    }

    private void LoadAdapters()
    {
        var adapters = AdapterDiscovery.GetAll();
        _adapterVms  = adapters.Select(a => new AdapterItemVm(a)).ToList();
        AdapterList.ItemsSource = _adapterVms;
    }

    private void AdapterItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not AdapterItemVm vm) return;

        foreach (var item in _adapterVms) item.IsSelected = false;
        vm.IsSelected   = true;
        _selectedAdapter = vm.Adapter;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Executables (*.exe)|*.exe|All files (*.*)|*.*",
            Title  = "Select Process Executable"
        };
        if (dlg.ShowDialog() != true) return;
        ProcessPathBox.Text = dlg.FileName;
        if (string.IsNullOrWhiteSpace(ProcessNameBox.Text))
            ProcessNameBox.Text = System.IO.Path.GetFileName(dlg.FileName);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var processName = ProcessNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(processName))
        {
            MessageBox.Show("Please enter a process name.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_selectedAdapter == null)
        {
            MessageBox.Show("Please select a network adapter.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _profile.AddRule(
            processName:  processName,
            adapterId:    _selectedAdapter.Id,
            adapterName:  _selectedAdapter.Name,
            processPath:  string.IsNullOrWhiteSpace(ProcessPathBox.Text) ? null : ProcessPathBox.Text.Trim(),
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

// ─── Adapter item view model ────────────────────────────────────────────────

internal class AdapterItemVm : System.ComponentModel.INotifyPropertyChanged
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

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new(name));
}
