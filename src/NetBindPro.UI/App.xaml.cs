// Copyright (c) 2024 NetBind Pro Contributors
// Licensed under the MIT License. See LICENSE in the project root for details.

using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetBindPro.Core.Services;
using NetBindPro.UI.Services;
using NetBindPro.UI.ViewModels;
using NetBindPro.UI.Views;
using Application = System.Windows.Application;

namespace NetBindPro.UI;

public partial class App : Application
{
    private ServiceProvider? _services;
    private NotifyIcon?      _trayIcon;
    private MainWindow?      _mainWindow;

    // ─── Startup ─────────────────────────────────────────────────────────────

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Build DI container
        _services = ConfigureServices().BuildServiceProvider();

        // Start background services
        _services.GetRequiredService<ProcessMonitorService>().Start();

        // Bring up any tunnels flagged auto-start (non-blocking).
        _ = StartAutoStartTunnelsAsync();

        // System tray icon
        InitTrayIcon();

        // Show main window unless started minimized
        bool startMinimized = e.Args.Contains("--minimized");
        if (!startMinimized)
            ShowMainWindow();
    }

    // ─── Auto-start tunnels ──────────────────────────────────────────────────

    private async Task StartAutoStartTunnelsAsync()
    {
        if (!WireGuardService.IsWireGuardInstalled) return;

        var profile = _services!.GetRequiredService<ProfileService>();
        var wg      = _services!.GetRequiredService<WireGuardService>();
        var logger  = _services!.GetService<ILoggerFactory>()?.CreateLogger("AutoStart");

        foreach (var tunnel in profile.Tunnels.Where(t => t.AutoStart).ToList())
        {
            try
            {
                var (ok, message) = await wg.StartAsync(tunnel);
                if (!ok)
                    logger?.LogWarning("Auto-start failed for {Name}: {Msg}", tunnel.Name, message);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Auto-start error for {Name}", tunnel.Name);
            }
        }
    }

    // ─── DI configuration ────────────────────────────────────────────────────

    private static ServiceCollection ConfigureServices()
    {
        var services = new ServiceCollection();

        // Logging
        services.AddLogging(b => b
            .AddDebug()
            .SetMinimumLevel(LogLevel.Debug));

        // Core services
        services.AddSingleton<ProfileService>();
        services.AddSingleton<InjectionService>();
        services.AddSingleton<ProcessMonitorService>();
        services.AddSingleton<WireGuardService>();

        // ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddTransient<RulesViewModel>();
        services.AddTransient<AdaptersViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<LogViewModel>();

        // Pages
        services.AddTransient<RulesPage>();
        services.AddTransient<AdaptersPage>();
        services.AddTransient<TunnelsPage>();
        services.AddTransient<ProcessesPage>();
        services.AddTransient<LogPage>();
        services.AddTransient<SettingsPage>();

        // Windows
        services.AddTransient<MainWindow>();
        services.AddTransient<AddRuleWindow>();

        return services;
    }

    // ─── Tray icon ───────────────────────────────────────────────────────────

    private void InitTrayIcon()
    {
        var iconPath = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!,
            "app.ico");
        _trayIcon = new NotifyIcon
        {
            Icon    = System.IO.File.Exists(iconPath) ? new Icon(iconPath) : SystemIcons.Application,
            Text    = "NetBind Pro",
            Visible = true
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open NetBind Pro",  null, (_, _) => ShowMainWindow());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit",              null, (_, _) => ExitApplication());

        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => ShowMainWindow();
    }

    // ─── Window management ───────────────────────────────────────────────────

    internal void ShowMainWindow()
    {
        if (_mainWindow == null || !_mainWindow.IsLoaded)
        {
            _mainWindow = _services!.GetRequiredService<MainWindow>();
            _mainWindow.Closing += OnMainWindowClosing;
        }

        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    private void OnMainWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var settings = _services!.GetRequiredService<ProfileService>().Settings;

        if (settings.MinimizeToTray)
        {
            e.Cancel = true;
            _mainWindow!.Hide();
            _trayIcon?.ShowBalloonTip(2000, "NetBind Pro",
                "Running in the background. Double-click to reopen.", ToolTipIcon.Info);
        }
    }

    // ─── Shutdown ────────────────────────────────────────────────────────────

    private void ExitApplication()
    {
        _services?.GetService<ProcessMonitorService>()?.Stop();
        _trayIcon?.Dispose();
        _services?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        _services?.Dispose();
        base.OnExit(e);
    }

    // ─── Helper ──────────────────────────────────────────────────────────────

    public static T GetService<T>() where T : notnull
        => ((App)Current)._services!.GetRequiredService<T>();
}
