// Copyright (c) 2024 NetBind Pro Contributors
// Licensed under the MIT License. See LICENSE in the project root for details.

using System.Diagnostics;
using System.IO;
using System.Reflection;
using Microsoft.Extensions.Logging;
using NetBindPro.Core.Models;

namespace NetBindPro.UI.Services;

/// <summary>
/// Launches <c>NetBindPro.Hook.exe</c> as a subprocess to inject the binding
/// payload into target processes. Using a subprocess isolates the hook's
/// administrator elevation from the UI process.
/// </summary>
public sealed class InjectionService
{
    private readonly ILogger<InjectionService> _logger;
    private readonly string _hookExePath;

    public InjectionService(ILogger<InjectionService> logger)
    {
        _logger = logger;

        // Hook.exe lives alongside the UI exe
        var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        _hookExePath = Path.Combine(dir, "NetBindPro.Hook.exe");
    }

    /// <summary>
    /// Injects the binding hook into an already-running process.
    /// </summary>
    public async Task<InjectionResult> InjectAsync(int pid, string adapterId, string adapterName)
    {
        _logger.LogInformation("Injecting PID {Pid} → adapter {Adapter}", pid, adapterName);

        var args = $"--network \"{adapterId}\" --attach {pid}";
        return await RunHookAsync(args, pid, adapterName);
    }

    /// <summary>
    /// Starts a new process and injects the binding hook into it immediately.
    /// </summary>
    public async Task<InjectionResult> ExecuteAndInjectAsync(
        string exePath, string adapterId, string adapterName,
        string arguments = "", int delayMs = 500, bool debug = false)
    {
        _logger.LogInformation("Execute+inject {Exe} → adapter {Adapter}", exePath, adapterName);

        var args = $"--network \"{adapterId}\" --execute \"{exePath}\" --delay {delayMs}";
        if (!string.IsNullOrEmpty(arguments))
            args += $" --args \"{arguments}\"";
        if (debug)
            args += " --debug";

        return await RunHookAsync(args, 0, adapterName);
    }

    // ─── Private ─────────────────────────────────────────────────────────────

    private async Task<InjectionResult> RunHookAsync(string args, int pid, string adapterName)
    {
        if (!File.Exists(_hookExePath))
        {
            var msg = $"Hook executable not found at: {_hookExePath}";
            _logger.LogError(msg);
            return InjectionResult.Fail(pid, string.Empty, msg);
        }

        try
        {
            var psi = new ProcessStartInfo(_hookExePath, args)
            {
                UseShellExecute        = true,
                Verb                   = "runas",   // elevate to admin
                CreateNoWindow         = true,
                WindowStyle            = ProcessWindowStyle.Hidden
            };

            var proc = Process.Start(psi) ?? throw new Exception("Failed to start Hook process.");
            await proc.WaitForExitAsync();

            if (proc.ExitCode == 0)
            {
                _logger.LogInformation("Injection success: PID {Pid} → {Adapter}", pid, adapterName);
                return InjectionResult.Ok(pid, string.Empty, adapterName);
            }

            var msg = $"Hook process exited with code {proc.ExitCode}";
            _logger.LogWarning(msg);
            return InjectionResult.Fail(pid, string.Empty, msg);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Injection failed");
            return InjectionResult.Fail(pid, string.Empty, ex.Message);
        }
    }
}
