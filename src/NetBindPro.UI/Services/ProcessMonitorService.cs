// Copyright (c) 2024 NetBind Pro Contributors
// Licensed under the MIT License. See LICENSE in the project root for details.

using System.Management;
using Microsoft.Extensions.Logging;
using NetBindPro.Core.Services;

namespace NetBindPro.UI.Services;

/// <summary>
/// Monitors the system for new process creation events using WMI.
/// When a process matching an enabled auto-inject rule starts, automatically
/// calls <see cref="InjectionService"/> to bind it to the configured adapter.
/// </summary>
public sealed class ProcessMonitorService : IDisposable
{
    private readonly ProfileService   _profile;
    private readonly InjectionService _injection;
    private readonly ILogger<ProcessMonitorService> _logger;

    private ManagementEventWatcher? _watcher;
    private bool _running;

    public ProcessMonitorService(
        ProfileService profile,
        InjectionService injection,
        ILogger<ProcessMonitorService> logger)
    {
        _profile   = profile;
        _injection = injection;
        _logger    = logger;
    }

    /// <summary>Starts listening for new process creation events.</summary>
    public void Start()
    {
        if (_running) return;
        _running = true;

        try
        {
            // WMI Win32_ProcessStartTrace fires on every process creation
            var query = new WqlEventQuery(
                "SELECT * FROM Win32_ProcessStartTrace");

            _watcher = new ManagementEventWatcher(query);
            _watcher.EventArrived += OnProcessStarted;
            _watcher.Start();

            _logger.LogInformation("Process monitor started (WMI)");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WMI process monitor unavailable — auto-inject disabled");
        }
    }

    /// <summary>Stops the process monitor.</summary>
    public void Stop()
    {
        _running = false;
        _watcher?.Stop();
        _watcher?.Dispose();
        _watcher = null;
        _logger.LogInformation("Process monitor stopped");
    }

    public void Dispose() => Stop();

    // ─── Event handler ────────────────────────────────────────────────────────

    private async void OnProcessStarted(object sender, EventArrivedEventArgs e)
    {
        try
        {
            var processName = e.NewEvent.Properties["ProcessName"]?.Value?.ToString();
            if (string.IsNullOrEmpty(processName)) return;

            var rule = _profile.FindRuleForProcess(processName);
            if (rule == null || !rule.AutoInject) return;

            // Small delay so the process has time to fully initialise its runtime
            var delay = Math.Max(_profile.Settings.InjectionDelayMs, 500);
            await Task.Delay(delay);

            var pidObj = e.NewEvent.Properties["ProcessID"]?.Value;
            if (pidObj == null) return;
            var pid = Convert.ToInt32(pidObj);

            // Never inject a sandboxed browser child (ACG) — it would crash it.
            if (InjectionService.IsSandboxedChild(pid))
            {
                _logger.LogDebug("Skipping sandboxed child {Process} (PID {Pid})", processName, pid);
                return;
            }

            // Resolve the live adapter — honours a tunnel-by-name rule even if the
            // tunnel's GUID changed since the rule was created.
            var (adapterId, adapterName) = WireGuardService.ResolveRuleAdapter(rule);

            _logger.LogInformation(
                "Auto-injecting {Process} (PID {Pid}) → {Adapter}",
                processName, pid, adapterName);

            var result = await _injection.InjectAsync(pid, adapterId, adapterName, rule.KillSwitch);
            if (!result.Success)
                _logger.LogWarning("Auto-inject failed for {Process}: {Msg}", processName, result.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in OnProcessStarted handler");
        }
    }
}
