// Copyright (c) 2024 NetBind Pro Contributors
// Licensed under the MIT License. See LICENSE in the project root for details.

using System.Diagnostics;
using System.IO;
using System.Management;
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

        // Hook.exe lives in a "hook" subfolder — it's a .NET Framework 4.8 build and
        // carries old framework-shim DLLs (System.Memory 4.0.1.1, etc.) that would
        // otherwise overwrite the self-contained .NET 10 UI's runtime assemblies and
        // crash WPF on launch. Keeping it isolated avoids that collision.
        var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        _hookExePath = Path.Combine(dir, "hook", "NetBindPro.Hook.exe");
        if (!File.Exists(_hookExePath))
            _hookExePath = Path.Combine(dir, "NetBindPro.Hook.exe"); // fallback (dev layout)
    }

    /// <summary>
    /// Injects the binding hook into an already-running process.
    /// </summary>
    public async Task<InjectionResult> InjectAsync(int pid, string adapterId, string adapterName, bool killSwitch = true)
    {
        _logger.LogInformation("Injecting PID {Pid} → adapter {Adapter} (kill-switch {Ks})", pid, adapterName, killSwitch);

        var args = $"--network \"{adapterId}\" --attach {pid}";
        if (!killSwitch) args += " --no-killswitch";
        return await RunHookAsync(args, pid, adapterName);
    }

    /// <summary>
    /// Starts a new process and injects the binding hook into it immediately.
    /// </summary>
    public async Task<InjectionResult> ExecuteAndInjectAsync(
        string exePath, string adapterId, string adapterName,
        string arguments = "", int delayMs = 500, bool debug = false, bool killSwitch = true)
    {
        _logger.LogInformation("Execute+inject {Exe} → adapter {Adapter}", exePath, adapterName);

        var args = $"--network \"{adapterId}\" --execute \"{exePath}\" --delay {delayMs}";
        if (!string.IsNullOrEmpty(arguments))
            args += $" --args \"{arguments}\"";
        if (debug)
            args += " --debug";
        if (!killSwitch)
            args += " --no-killswitch";

        return await RunHookAsync(args, 0, adapterName);
    }

    // ─── Sandboxed-child detection ───────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> if the process is a sandboxed browser child (Chromium renderer/
    /// GPU/crashpad, or a Firefox content tab). Those enable Arbitrary Code Guard (ACG),
    /// which makes DLL injection crash them — so we must NOT inject them. The browser's
    /// main process and its network/utility process (where traffic actually flows) are fine.
    /// </summary>
    public static bool IsSandboxedChild(int pid)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}");
            foreach (ManagementBaseObject mo in searcher.Get())
            {
                var cl = mo["CommandLine"]?.ToString() ?? string.Empty;
                return IsSandboxedCommandLine(cl);
            }
        }
        catch { /* if we can't tell, don't block injection */ }
        return false;
    }

    private static bool IsSandboxedCommandLine(string cl)
    {
        // Chromium (Chrome/Edge/Brave) sandboxed children
        if (cl.Contains("--type=renderer")) return true;
        if (cl.Contains("--type=gpu-process")) return true;
        if (cl.Contains("--type=crashpad-handler")) return true;
        if (cl.Contains("--type=ppapi")) return true;
        // Firefox content tab processes (keep the main + socket process injectable)
        if (cl.Contains("-contentproc") &&
            (cl.TrimEnd().EndsWith(" tab", StringComparison.OrdinalIgnoreCase) ||
             cl.Contains(" tab ")))
            return true;
        return false;
    }

    /// <summary>
    /// Returns the PIDs of a named process that are safe and useful to inject —
    /// the main and network/utility processes, with sandboxed children filtered out.
    /// </summary>
    public static List<int> GetInjectablePids(string processName)
    {
        var baseName = Path.GetFileNameWithoutExtension(processName);
        return Process.GetProcessesByName(baseName)
            .Select(p => p.Id)
            .Where(pid => !IsSandboxedChild(pid))
            .ToList();
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
