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
    private bool            _isExiting;   // true once a real shutdown is underway

    // ─── Startup ─────────────────────────────────────────────────────────────

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Surface any otherwise-silent failure instead of a process that never shows a window.
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
            ReportFatal("A fatal error occurred.", ex.ExceptionObject as Exception);
        DispatcherUnhandledException += (_, ex) =>
        {
            ReportFatal("A fatal error occurred.", ex.Exception);
            ex.Handled = true;
        };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, ex) =>
        {
            LogCrash("Unobserved task exception", ex.Exception);
            ex.SetObserved();
        };

        try
        {
            // Build DI container
            _services = ConfigureServices().BuildServiceProvider();

            // Start background services
            _services.GetRequiredService<ProcessMonitorService>().Start();

            // Bring up any tunnels flagged auto-start (non-blocking).
            _ = StartAutoStartTunnelsAsync();

            // Check for a newer release in the background (non-blocking).
            _ = CheckForUpdatesAsync(prompt: true);

            // System tray icon
            InitTrayIcon();

            // Show main window unless started minimized
            bool startMinimized = e.Args.Contains("--minimized");
            if (!startMinimized)
                ShowMainWindow();
        }
        catch (Exception ex)
        {
            ReportFatal("NetBind Pro failed to start.", ex);
            Shutdown(1);
        }
    }

    // ─── Crash reporting ─────────────────────────────────────────────────────

    private static string CrashLogPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NetBindPro", "crash.log");

    private static void LogCrash(string context, Exception? ex)
    {
        try
        {
            var path = CrashLogPath;
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            System.IO.File.AppendAllText(path,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { /* best-effort */ }
    }

    private static void ReportFatal(string context, Exception? ex)
    {
        LogCrash(context, ex);
        try
        {
            System.Windows.MessageBox.Show(
                $"{context}\n\n{ex?.Message}\n\nDetails were written to:\n{CrashLogPath}",
                "NetBind Pro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch { /* headless */ }
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

    // ─── Updates ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Checks GitHub for a newer release. When <paramref name="prompt"/> is true and an
    /// update exists, offers to download and run it. Returns true if an update was found.
    /// </summary>
    internal async Task<bool> CheckForUpdatesAsync(bool prompt)
    {
        try
        {
            var svc    = _services!.GetRequiredService<UpdateService>();
            var update = await svc.CheckForUpdateAsync();
            if (update == null) return false;

            if (prompt)
            {
                var answer = System.Windows.MessageBox.Show(
                    $"NetBind Pro {update.Version} is available (you have {UpdateService.CurrentVersion}).\n\nDownload and install it now?",
                    "Update available", MessageBoxButton.YesNo, MessageBoxImage.Information);

                if (answer == MessageBoxResult.Yes)
                {
                    var (ok, message) = await svc.DownloadAndRunAsync(update);
                    System.Windows.MessageBox.Show(message, "NetBind Pro",
                        MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
                    if (ok) ExitApplication();
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            LogCrash("Update check", ex);
            return false;
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
        services.AddSingleton<UpdateService>();

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
        // A real exit (tray → Exit, or the installer closing us) disposes the service
        // provider first; don't touch it then, and let the window close. Without this the
        // closing handler hit a disposed IServiceProvider and threw ObjectDisposedException,
        // which surfaced as a crash dialog every time the app was closed.
        if (_isExiting) return;

        try
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
        catch (ObjectDisposedException)
        {
            // Services already gone — we're shutting down; just let it close.
        }
    }

    // ─── Shutdown ────────────────────────────────────────────────────────────

    private void ExitApplication()
    {
        _isExiting = true;   // tell OnMainWindowClosing not to touch disposed services
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
