// Copyright (c) 2024 NetBind Pro Contributors
// Licensed under the MIT License. See LICENSE in the project root for details.

using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using NetBindPro.Core.Models;
using NetBindPro.Core.Services;
using NetBindPro.UI.Services;
using NetBindPro.UI.ViewModels;

namespace NetBindPro.UI.Views;

// ─── AdaptersPage ────────────────────────────────────────────────────────────

public partial class AdaptersPage : Page
{
    public AdaptersPage()
    {
        InitializeComponent();
        Loaded += (_, _) => Load();
    }

    private void Load()
    {
        var items = AdapterDiscovery.GetAll()
            .Select(a => new AdapterCardVm(a))
            .ToList();

        AdapterGrid.ItemsSource = items;
        CountBadge.Text = items.Count.ToString();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Load();
}

public class AdapterCardVm
{
    public AdapterInfo Adapter    { get; }
    public string      KindLabel  { get; }
    public string      AccentColor { get; }
    public string      BadgeBackground { get; }
    public string      HasIpV6Visibility { get; }

    public AdapterCardVm(AdapterInfo adapter)
    {
        Adapter = adapter;
        HasIpV6Visibility = !string.IsNullOrEmpty(adapter.IpV6Address) ? "Visible" : "Collapsed";

        (KindLabel, AccentColor, BadgeBackground) = adapter.Kind switch
        {
            AdapterKind.WireGuard => ("WireGuard", "#0284C7", "#120284C7"),
            AdapterKind.Tailscale => ("Tailscale",  "#2563EB", "#122563EB"),
            AdapterKind.Vpn       => ("VPN",         "#7C3AED", "#127C3AED"),
            AdapterKind.WiFi      => ("Wi-Fi",       "#0D9488", "#120D9488"),
            AdapterKind.Ethernet  => ("Ethernet",    "#555555", "#12555555"),
            _                     => ("Other",       "#555555", "#12555555")
        };
    }
}

// ─── ProcessesPage ───────────────────────────────────────────────────────────

public partial class ProcessesPage : Page
{
    public ProcessesPage() { InitializeComponent(); Loaded += (_, _) => Load(); }

    private void Load()
    {
        var procs = System.Diagnostics.Process.GetProcesses()
            .Where(p => { try { return !string.IsNullOrEmpty(p.MainWindowTitle) || p.Responding; } catch { return false; } })
            .OrderBy(p => p.ProcessName)
            .Select(p => new ProcessRowVm(p))
            .ToList();

        ProcessList.ItemsSource = procs;
        CountBadge.Text = procs.Count.ToString();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Load();

    // One-off inject from the process list: pick an adapter, then bind the chosen
    // process (and its injectable siblings, e.g. a browser's network service) to it.
    private async void Inject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not ProcessRowVm row) return;

        var adapter = AdapterPickerWindow.Pick(Window.GetWindow(this), row.Name);
        if (adapter == null) return;

        var injection = App.GetService<InjectionService>();

        // Prefer all injectable PIDs of this name (skips sandboxed browser children that
        // would crash); fall back to the exact PID the row represents.
        var pids = InjectionService.GetInjectablePids(row.Name);
        if (pids.Count == 0) pids = [row.Pid];

        var failures = 0;
        foreach (var pid in pids)
        {
            var result = await injection.InjectAsync(pid, adapter.Id, adapter.Name);
            if (!result.Success) failures++;
        }

        if (failures == pids.Count)
            MessageBox.Show(
                $"Couldn't bind the running '{row.Name}'.\n\n" +
                "Browsers and other hardened apps block being bound after they have started " +
                "— add a rule for them and use its Launch button to start them bound instead.",
                "NetBind Pro", MessageBoxButton.OK, MessageBoxImage.Warning);
        else
            MessageBox.Show($"'{row.Name}' is now bound to {adapter.Name}" +
                            (failures > 0 ? $" ({failures} of {pids.Count} process(es) failed)." : "."),
                "NetBind Pro", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}

public class ProcessRowVm
{
    public string Name    { get; }
    public int    Pid     { get; }
    public string PidText => $"PID {Pid}";
    public string Arch    { get; }

    public ProcessRowVm(System.Diagnostics.Process p)
    {
        Name = p.ProcessName + ".exe";
        Pid  = p.Id;
        Arch = DetectArch(p.Id);
    }

    // Real per-PROCESS bitness (the old code reported OS bitness, so everything showed x64).
    // Injection requires an architecture-matched hook, so this needs to be accurate.
    private const int    PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const ushort IMAGE_FILE_MACHINE_UNKNOWN = 0x0000;
    private const ushort IMAGE_FILE_MACHINE_I386    = 0x014C;
    private const ushort IMAGE_FILE_MACHINE_AMD64   = 0x8664;
    private const ushort IMAGE_FILE_MACHINE_ARM64   = 0xAA64;

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int access, bool inherit, int pid);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool IsWow64Process2(IntPtr hProcess, out ushort processMachine, out ushort nativeMachine);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr h);

    private static string DetectArch(int pid)
    {
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return "?";
        try
        {
            if (!IsWow64Process2(h, out var procMachine, out var nativeMachine)) return "?";
            // procMachine == UNKNOWN means the process runs natively → use the OS machine.
            var machine = procMachine == IMAGE_FILE_MACHINE_UNKNOWN ? nativeMachine : procMachine;
            return machine switch
            {
                IMAGE_FILE_MACHINE_I386  => "x86",
                IMAGE_FILE_MACHINE_AMD64 => "x64",
                IMAGE_FILE_MACHINE_ARM64 => "ARM64",
                _                        => "?"
            };
        }
        catch { return "?"; }
        finally { CloseHandle(h); }
    }
}

// ─── LogPage ─────────────────────────────────────────────────────────────────

public partial class LogPage : Page
{
    private readonly LogViewModel _vm;

    public LogPage(LogViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
    }

    private void Clear_Click(object sender, RoutedEventArgs e) => _vm.Clear();
}
