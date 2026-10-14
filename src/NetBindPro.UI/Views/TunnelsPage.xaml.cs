// Copyright (c) 2024 NetBind Pro Contributors
// Licensed under the MIT License. See LICENSE in the project root for details.

using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using NetBindPro.Core.Models;
using NetBindPro.Core.Services;

namespace NetBindPro.UI.Views;

public partial class TunnelsPage : Page
{
    private readonly ProfileService   _profile;
    private readonly WireGuardService _wg;

    public TunnelsPage(ProfileService profile, WireGuardService wg)
    {
        _profile = profile;
        _wg      = wg;
        InitializeComponent();
        Loaded += (_, _) => Reload();
    }

    // ─── Load / refresh ──────────────────────────────────────────────────────

    private void Reload()
    {
        MissingBanner.Visibility = WireGuardService.IsWireGuardInstalled
            ? Visibility.Collapsed : Visibility.Visible;

        var vms = _profile.Tunnels
            .Select(t => new TunnelItemVm(t, _wg.GetState(t)))
            .ToList();

        TunnelList.ItemsSource    = vms;
        CountBadge.Text           = vms.Count.ToString();
        EmptyState.Visibility     = vms.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Reload();

    // ─── Import ──────────────────────────────────────────────────────────────

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter      = "WireGuard config (*.conf)|*.conf|All files (*.*)|*.*",
            Title       = "Import WireGuard Tunnel",
            Multiselect = true
        };
        if (dlg.ShowDialog() != true) return;

        foreach (var file in dlg.FileNames)
        {
            try
            {
                var tunnel = _wg.ImportConfig(file);
                _profile.AddTunnel(tunnel);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not import '{Path.GetFileName(file)}':\n{ex.Message}",
                    "Import failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        Reload();
    }

    // ─── Install WireGuard ───────────────────────────────────────────────────

    private async void InstallWg_Click(object sender, RoutedEventArgs e)
    {
        InstallWgButton.IsEnabled = false;
        var progress = new Progress<string>(msg => MissingBannerSub.Text = msg);

        try
        {
            var (ok, message) = await _wg.EnsureWireGuardInstalledAsync(progress);
            MissingBannerSub.Text = message;
            if (!ok)
                MessageBox.Show(message, "WireGuard", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "WireGuard", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            InstallWgButton.IsEnabled = true;
            Reload();
        }
    }

    // ─── Start / stop ────────────────────────────────────────────────────────

    private async void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (ResolveVm(sender) is not { } vm) return;

        if (!WireGuardService.IsWireGuardInstalled)
        {
            MessageBox.Show("Install WireGuard for Windows first.", "WireGuard",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Capture the running state BEFORE SetBusy() flips it to "Starting",
        // otherwise the Stop branch is never taken.
        var wasRunning = vm.IsRunning;
        vm.SetBusy();
        try
        {
            var (ok, message) = wasRunning
                ? await _wg.StopAsync(vm.Tunnel)
                : await _wg.StartAsync(vm.Tunnel);

            if (!ok)
                MessageBox.Show(message, "Tunnel", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Tunnel", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        // Give the service a moment to settle, then refresh its state.
        await Task.Delay(600);
        vm.UpdateState(_wg.GetState(vm.Tunnel));
    }

    // ─── Exit IP ─────────────────────────────────────────────────────────────

    private async void CheckIp_Click(object sender, RoutedEventArgs e)
    {
        if (ResolveVm(sender) is not { } vm) return;

        var adapter = WireGuardService.ResolveAdapter(vm.Tunnel.Name);
        if (adapter == null)
        {
            vm.SetExitIp("Tunnel adapter not found (is it running?)");
            return;
        }

        vm.SetExitIp("Checking…");
        try
        {
            var r = await EgressCheckService.CheckAsync(adapter);
            vm.SetExitIp(r.Success
                ? $"Exit IP: {r.PublicIp}{(string.IsNullOrEmpty(r.Country) ? "" : $" ({r.Country})")}"
                : $"Failed: {r.Error}");
        }
        catch (Exception ex)
        {
            vm.SetExitIp($"Failed: {ex.Message}");
        }
    }

    // ─── Auto-start ──────────────────────────────────────────────────────────

    private void AutoStart_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox cb || ResolveVm(cb) is not { } vm) return;

        var enabled = cb.IsChecked == true;
        if (vm.Tunnel.AutoStart == enabled) return; // no-op (e.g. fired during load)

        vm.Tunnel.AutoStart = enabled;
        _profile.UpdateTunnel(vm.Tunnel);
    }

    // ─── Auto-MTU ────────────────────────────────────────────────────────────

    private async void AutoMtu_Click(object sender, RoutedEventArgs e)
    {
        if (ResolveVm(sender) is not { } vm) return;

        vm.SetExitIp("Probing MTU…");
        try
        {
            var (ok, mtu, message) = await WireGuardService.ProbeMtuAsync(vm.Tunnel.Endpoint);
            if (!ok)
            {
                // Probe failed (e.g. endpoint blocks ping) — don't silently rewrite the MTU.
                vm.SetExitIp(message);
                return;
            }

            _wg.SetMtu(vm.Tunnel, mtu);
            _profile.UpdateTunnel(vm.Tunnel);

            var restart = vm.IsRunning ? " Restart the tunnel to apply." : "";
            vm.SetExitIp($"MTU set to {mtu}. {message}{restart}");
        }
        catch (Exception ex)
        {
            vm.SetExitIp($"MTU probe failed: {ex.Message}");
        }
    }

    // ─── Delete ──────────────────────────────────────────────────────────────

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (ResolveVm(sender) is not { } vm) return;

        var confirm = MessageBox.Show(
            $"Remove tunnel '{vm.Tunnel.Name}'?\nThis stops it and deletes its stored config.",
            "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            if (vm.IsRunning && WireGuardService.IsWireGuardInstalled)
                await _wg.StopAsync(vm.Tunnel);

            if (File.Exists(vm.Tunnel.ConfigPath))
                File.Delete(vm.Tunnel.ConfigPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Tunnel", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        _profile.RemoveTunnel(vm.Tunnel.Id);
        Reload();
    }

    // ─── Helper ──────────────────────────────────────────────────────────────

    private static TunnelItemVm? ResolveVm(object sender)
        => (sender as FrameworkElement)?.DataContext as TunnelItemVm;
}

// ─── Tunnel row view model ──────────────────────────────────────────────────

internal sealed class TunnelItemVm : INotifyPropertyChanged
{
    public TunnelConfig Tunnel { get; }

    private TunnelState _state;
    private string?     _exitIp;

    public TunnelItemVm(TunnelConfig tunnel, TunnelState state)
    {
        Tunnel = tunnel;
        _state = state;
    }

    public string Name => Tunnel.Name;

    public bool IsAutoStart => Tunnel.AutoStart;

    public string Subtitle
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Tunnel.Address))  parts.Add(Tunnel.Address!);
            if (!string.IsNullOrWhiteSpace(Tunnel.Endpoint)) parts.Add("→ " + Tunnel.Endpoint);
            return parts.Count > 0 ? string.Join("   ", parts) : Tunnel.ConfigPath;
        }
    }

    public bool   IsRunning   => _state is TunnelState.Running;
    public string ToggleLabel => _state switch
    {
        TunnelState.Running  => "Stop",
        TunnelState.Starting => "…",
        _                    => "Start"
    };

    public string StateText => _state switch
    {
        TunnelState.Running  => "ACTIVE",
        TunnelState.Starting => "STARTING",
        TunnelState.Error    => "ERROR",
        _                    => "STOPPED"
    };

    public string StateColor => _state switch
    {
        TunnelState.Running  => "#16A34A",
        TunnelState.Starting => "#D97706",
        TunnelState.Error    => "#DC2626",
        _                    => "#9AA0A6"
    };

    public string StateBg => _state switch
    {
        TunnelState.Running  => "#1216A34A",
        TunnelState.Starting => "#12D97706",
        TunnelState.Error    => "#12DC2626",
        _                    => "#12555555"
    };

    public string ExitIpText       => _exitIp ?? string.Empty;
    public string ExitIpVisibility => string.IsNullOrEmpty(_exitIp) ? "Collapsed" : "Visible";

    public void UpdateState(TunnelState state)
    {
        _state = state;
        RaiseAll();
    }

    public void SetBusy()
    {
        _state = TunnelState.Starting;
        RaiseAll();
    }

    public void SetExitIp(string text)
    {
        _exitIp = text;
        OnPropertyChanged(nameof(ExitIpText));
        OnPropertyChanged(nameof(ExitIpVisibility));
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(ToggleLabel));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(StateColor));
        OnPropertyChanged(nameof(StateBg));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new(name));
}
