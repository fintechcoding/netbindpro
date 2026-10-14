// Copyright (c) 2024 NetBind Pro Contributors
// Licensed under the MIT License. See LICENSE in the project root for details.

using System.Windows;
using System.Windows.Input;
using NetBindPro.Core.Models;
using NetBindPro.Core.Services;
using NetBindPro.UI.ViewModels;

namespace NetBindPro.UI.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow(MainViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
        NavigateTo("Rules");
        UpdateStatusBar();

        var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        if (v != null) VersionText.Text = $"v{v.Major}.{v.Minor}.{v.Build}";
    }

    // ─── Navigation ──────────────────────────────────────────────────────────

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn)
            NavigateTo(btn.Tag?.ToString() ?? "Rules");
    }

    private void NavigateTo(string page)
    {
        // Deactivate all nav buttons
        foreach (var child in FindVisualChildren<System.Windows.Controls.Button>(this))
        {
            if (child.Tag is string tag && new[] { "Rules", "Adapters", "Tunnels", "Processes", "Log", "Settings" }.Contains(tag))
                child.IsEnabled = true;
        }

        PageFrame.Navigate(page switch
        {
            "Rules"     => App.GetService<RulesPage>(),
            "Adapters"  => App.GetService<AdaptersPage>(),
            "Tunnels"   => App.GetService<TunnelsPage>(),
            "Processes" => App.GetService<ProcessesPage>(),
            "Log"       => App.GetService<LogPage>(),
            "Settings"  => App.GetService<SettingsPage>(),
            _           => App.GetService<RulesPage>()
        });
    }

    // ─── Status bar ──────────────────────────────────────────────────────────

    private void UpdateStatusBar()
    {
        var profile  = App.GetService<ProfileService>();
        var adapters = AdapterDiscovery.GetAll();

        var activeRules = profile.Rules.Count(r => r.IsEnabled);
        StatusRules.Text = $"{activeRules} rule{(activeRules != 1 ? "s" : "")} active";

        var wg = adapters.FirstOrDefault(a => a.Kind == AdapterKind.WireGuard);
        var ts = adapters.FirstOrDefault(a => a.Kind == AdapterKind.Tailscale);

        StatusWireGuard.Text = wg != null ? $"WireGuard: {wg.IpV4Address}" : string.Empty;
        StatusTailscale.Text = ts != null ? $"Tailscale: {ts.IpV4Address}" : string.Empty;
    }

    // ─── Window chrome ───────────────────────────────────────────────────────

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else
            DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e)
        => Close();

    // ─── Helper ──────────────────────────────────────────────────────────────

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T t) yield return t;
            foreach (var c in FindVisualChildren<T>(child)) yield return c;
        }
    }
}
