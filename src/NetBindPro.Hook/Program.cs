// Copyright (c) 2024 NetBind Pro Contributors
// Licensed under the MIT License. See LICENSE in the project root for details.

using System.CommandLine;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using EasyHook;

namespace NetBindPro.Hook;

/// <summary>
/// CLI entry point for NetBind Pro Hook.
/// Handles adapter validation, process creation/injection, and exposes
/// a --list command for adapter discovery.
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var rootCmd = BuildCommandLine();
        return await rootCmd.InvokeAsync(args);
    }

    // ─── Command definitions ─────────────────────────────────────────────────

    private static RootCommand BuildCommandLine()
    {
        var root = new RootCommand("NetBind Pro Hook — Per-process network adapter binding");

        // ── Options shared across subcommands ──
        var networkOpt = new Option<string>(
            ["--network", "-n"],
            "Network adapter GUID (use --list to find it)") { IsRequired = false };

        var executeOpt = new Option<FileInfo?>(
            ["--execute", "-e"],
            "Path to the executable to launch and bind");

        var attachOpt = new Option<int>(
            ["--attach", "-a"],
            "PID of a running process to bind");

        var argsOpt = new Option<string>(
            ["--args", "-c"],
            "Arguments to pass to the launched executable");

        var delayOpt = new Option<int>(
            ["--delay", "-t"],
            () => 500,
            "Milliseconds to wait before injecting (default: 500)");

        var debugOpt = new Option<bool>(
            ["--debug", "-d"],
            "Write a verbose log to %TEMP%\\NetBindPro-<process>.log");

        var noTitleOpt = new Option<bool>(
            "--no-title",
            "Do not modify the bound process's window title");

        var listCmd = new Command("list", "List all available network adapters");
        listCmd.AddAlias("--list");
        listCmd.SetHandler(HandleList);

        root.AddCommand(listCmd);
        root.AddOption(networkOpt);
        root.AddOption(executeOpt);
        root.AddOption(attachOpt);
        root.AddOption(argsOpt);
        root.AddOption(delayOpt);
        root.AddOption(debugOpt);
        root.AddOption(noTitleOpt);

        root.SetHandler(async (network, execute, attach, cmdArgs, delay, debug, noTitle) =>
        {
            await HandleBind(network, execute, attach, cmdArgs, delay, debug, !noTitle);
        }, networkOpt, executeOpt, attachOpt, argsOpt, delayOpt, debugOpt, noTitleOpt);

        return root;
    }

    // ─── Handlers ────────────────────────────────────────────────────────────

    private static void HandleList()
    {
        Console.WriteLine();
        Console.WriteLine("  NetBind Pro — Available Network Adapters");
        Console.WriteLine("  " + new string('-', 50));
        Console.WriteLine();

        var nics = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                        n.OperationalStatus == OperationalStatus.Up)
            .ToList();

        if (nics.Count == 0)
        {
            Console.WriteLine("  No active adapters found.");
            return;
        }

        foreach (var n in nics)
        {
            string? ipv4 = null;
            try
            {
                ipv4 = n.GetIPProperties().UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                    ?.Address.ToString();
            }
            catch { /* ignore */ }

            Console.Write($"  {n.Name,-24}");
            Console.WriteLine($" {ipv4 ?? "no IPv4"}");
            Console.WriteLine($"             GUID: {{{n.Id}}}");
            if (!string.IsNullOrEmpty(n.Description))
                Console.WriteLine($"             Desc: {n.Description}");
            Console.WriteLine();
        }
    }

    private static Task HandleBind(
        string? networkId, FileInfo? execute, int attach,
        string? cmdArgs, int delay, bool debug, bool changeTitle)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(networkId))
            {
                Console.Error.WriteLine("ERROR: --network (-n) is required. Run 'list' to see available adapters.");
                Environment.Exit(1);
                return Task.CompletedTask;
            }

            // Validate adapter — no SupportsMulticast filter
            var nic = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n =>
                    n.Id.Equals(networkId.Trim('{', '}'), StringComparison.OrdinalIgnoreCase) &&
                    n.OperationalStatus == OperationalStatus.Up);

            if (nic == null)
            {
                Console.Error.WriteLine($"ERROR: Adapter '{networkId}' not found or is not active.");
                Console.Error.WriteLine("       Run 'list' to see available adapters.");
                Environment.Exit(1);
                return Task.CompletedTask;
            }

            Console.WriteLine($"[NetBind Pro] Adapter : {nic.Name}");
            Console.WriteLine($"[NetBind Pro] GUID    : {{{nic.Id}}}");

            if (execute != null)
            {
                if (!execute.Exists)
                {
                    Console.Error.WriteLine($"ERROR: File not found: {execute.FullName}");
                    Environment.Exit(1);
                    return Task.CompletedTask;
                }

                Console.WriteLine($"[NetBind Pro] Execute : {execute.FullName}");
                ExecuteAndInject(nic.Id, execute.FullName, cmdArgs ?? string.Empty, delay, debug, changeTitle);
            }
            else if (attach > 0)
            {
                Console.WriteLine($"[NetBind Pro] Attach  : PID {attach}");
                InjectProcess(nic.Id, attach, delay, debug, changeTitle);
            }
            else
            {
                Console.Error.WriteLine("ERROR: Specify --execute or --attach.");
                Environment.Exit(1);
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"[NetBind Pro] FATAL: {ex.Message}");
            Console.ResetColor();
            Environment.Exit(1);
        }

        return Task.CompletedTask;
    }

    // ─── Injection logic ─────────────────────────────────────────────────────

    private static void ExecuteAndInject(
        string networkId, string exePath, string arguments,
        int delay, bool debug, bool changeTitle)
    {
        var injector = Assembly.GetExecutingAssembly().Location;

        if (delay <= 0)
        {
            // Fast path: CreateAndInject starts the process in a suspended state,
            // injects the hook DLL, then resumes — no race condition.
            try
            {
                Console.WriteLine("[NetBind Pro] Starting process (suspended inject)...");

                RemoteHooking.CreateAndInject(
                    exePath, arguments, 0,
                    injector, injector,
                    out var pid,
                    networkId, injector, delay, changeTitle, debug);

                Console.WriteLine($"[NetBind Pro] SUCCESS — PID {pid} bound to adapter");
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("[NetBind Pro] Process is running with network binding active.");
                Console.ResetColor();
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NetBind Pro] Suspended inject failed ({ex.Message}), using delayed inject...");
            }
        }

        // Delayed path: start process normally, wait, then inject
        var psi = new ProcessStartInfo(exePath, arguments) { UseShellExecute = true };
        var proc = Process.Start(psi) ?? throw new Exception("Failed to start process.");

        Console.WriteLine($"[NetBind Pro] Started PID {proc.Id} — waiting {Math.Max(delay, 1000)} ms...");
        Thread.Sleep(Math.Max(delay, 1000));

        InjectProcess(networkId, proc.Id, delay, debug, changeTitle);
    }

    private static void InjectProcess(
        string networkId, int pid, int delay, bool debug, bool changeTitle)
    {
        var injector = Assembly.GetExecutingAssembly().Location;

        Console.WriteLine($"[NetBind Pro] Injecting PID {pid}...");

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                RemoteHooking.Inject(
                    pid, injector, injector,
                    networkId, injector, delay, changeTitle, debug);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[NetBind Pro] SUCCESS — PID {pid} bound to adapter");
                Console.ResetColor();
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NetBind Pro] Attempt {attempt}/3 failed: {ex.Message}");
                if (attempt < 3) Thread.Sleep(1000);
            }
        }

        throw new Exception($"Failed to inject into PID {pid} after 3 attempts.");
    }
}
