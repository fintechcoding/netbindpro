// Copyright (c) 2024 NetBind Pro Contributors
// Licensed under the MIT License. See LICENSE in the project root for details.

using System.IO;
using System.Runtime.InteropServices;

namespace NetBindPro.UI.Services;

/// <summary>
/// Launches a child process at the interactive user's MEDIUM integrity level from
/// within this (elevated) process, by duplicating the shell's (explorer.exe) token
/// and calling <c>CreateProcessWithTokenW</c>.
/// </summary>
/// <remarks>
/// Why this exists: EasyHook's <c>CreateAndInject</c> starts the target process at the
/// Hook's own integrity level. If the Hook runs elevated (because the UI is elevated),
/// Chrome/Edge detect that they were launched elevated and <b>de-elevate themselves</b>
/// (relaunch as a fresh, medium-IL process — note <c>--do-not-de-elevate</c> on the
/// first instance). That relaunched process is NOT the one we injected, so the hook is
/// lost and "nothing gets bound". Launching the Hook de-elevated makes Chrome start at
/// medium IL in the first place, so the injected process is the real browser.
///
/// The UI stays elevated for the operations that genuinely need it (WMI process
/// monitoring, WireGuard tunnel service install/remove, routing); only the injection
/// launch is dropped to the user's level here.
/// </remarks>
internal static class DeElevation
{
    public sealed record LaunchResult(bool Started, int ExitCode, string? Error);

    /// <summary>
    /// Runs <paramref name="exePath"/> with <paramref name="arguments"/> at the shell's
    /// (medium) integrity level, waits up to <paramref name="timeoutMs"/> for it to exit,
    /// and returns its exit code. If the de-elevation primitives are unavailable (e.g. no
    /// interactive shell, or this process lacks SeImpersonatePrivilege), returns
    /// <c>Started = false</c> so the caller can fall back to a normal launch.
    /// </summary>
    public static LaunchResult Run(string exePath, string arguments, int timeoutMs = 120_000)
    {
        IntPtr shellToken = IntPtr.Zero, primaryToken = IntPtr.Zero, envBlock = IntPtr.Zero;
        var pi = new PROCESS_INFORMATION();
        try
        {
            var shell = GetShellWindow();
            if (shell == IntPtr.Zero)
                return new LaunchResult(false, -1, "No interactive shell (explorer) to de-elevate through.");

            GetWindowThreadProcessId(shell, out var shellPid);
            if (shellPid == 0)
                return new LaunchResult(false, -1, "Could not resolve the shell process id.");

            var hShell = OpenProcess(PROCESS_QUERY_INFORMATION, false, shellPid);
            if (hShell == IntPtr.Zero)
                return new LaunchResult(false, -1, $"OpenProcess(shell) failed ({Marshal.GetLastWin32Error()}).");

            try
            {
                if (!OpenProcessToken(hShell, TOKEN_DUPLICATE | TOKEN_QUERY, out shellToken))
                    return new LaunchResult(false, -1, $"OpenProcessToken failed ({Marshal.GetLastWin32Error()}).");
            }
            finally { CloseHandle(hShell); }

            if (!DuplicateTokenEx(shellToken, MAXIMUM_ALLOWED, IntPtr.Zero,
                    SecurityImpersonation, TokenPrimary, out primaryToken))
                return new LaunchResult(false, -1, $"DuplicateTokenEx failed ({Marshal.GetLastWin32Error()}).");

            // Give the child the user's full environment (TEMP, PATH, …) so Chrome and
            // EasyHook's native loaders behave exactly as a normally-launched process.
            uint creationFlags = CREATE_NO_WINDOW;
            if (CreateEnvironmentBlock(out envBlock, primaryToken, false))
                creationFlags |= CREATE_UNICODE_ENVIRONMENT;
            else
                envBlock = IntPtr.Zero;

            var si = new STARTUPINFO();
            si.cb = Marshal.SizeOf<STARTUPINFO>();

            var cmdLine = $"\"{exePath}\" {arguments}";
            var workDir = Path.GetDirectoryName(exePath);

            if (!CreateProcessWithTokenW(primaryToken, 0, exePath, cmdLine,
                    creationFlags, envBlock, workDir, ref si, out pi))
            {
                return new LaunchResult(false, -1, $"CreateProcessWithTokenW failed ({Marshal.GetLastWin32Error()}).");
            }

            // Wait for the Hook to finish injecting (it exits shortly after; the launched
            // Chrome keeps running as its own process).
            var wait = WaitForSingleObject(pi.hProcess, timeoutMs < 0 ? INFINITE : (uint)timeoutMs);
            if (wait != WAIT_OBJECT_0)
                return new LaunchResult(true, -1, "Timed out waiting for the Hook to finish.");

            GetExitCodeProcess(pi.hProcess, out var code);
            return new LaunchResult(true, unchecked((int)code), null);
        }
        catch (Exception ex)
        {
            return new LaunchResult(false, -1, ex.Message);
        }
        finally
        {
            if (pi.hThread != IntPtr.Zero) CloseHandle(pi.hThread);
            if (pi.hProcess != IntPtr.Zero) CloseHandle(pi.hProcess);
            if (envBlock != IntPtr.Zero) DestroyEnvironmentBlock(envBlock);
            if (primaryToken != IntPtr.Zero) CloseHandle(primaryToken);
            if (shellToken != IntPtr.Zero) CloseHandle(shellToken);
        }
    }

    // ─── Win32 interop ────────────────────────────────────────────────────────

    private const int  PROCESS_QUERY_INFORMATION = 0x0400;
    private const int  TOKEN_DUPLICATE = 0x0002;
    private const int  TOKEN_QUERY = 0x0008;
    private const int  MAXIMUM_ALLOWED = 0x02000000;
    private const int  SecurityImpersonation = 2;
    private const int  TokenPrimary = 1;
    private const uint CREATE_NO_WINDOW = 0x08000000;
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    private const uint INFINITE = 0xFFFFFFFF;
    private const uint WAIT_OBJECT_0 = 0x0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, int DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(IntPtr hExistingToken, int dwDesiredAccess,
        IntPtr lpTokenAttributes, int ImpersonationLevel, int TokenType, out IntPtr phNewToken);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessWithTokenW(IntPtr hToken, int dwLogonFlags,
        string? lpApplicationName, string lpCommandLine, uint dwCreationFlags,
        IntPtr lpEnvironment, string? lpCurrentDirectory, ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool CreateEnvironmentBlock(out IntPtr lpEnvironment, IntPtr hToken, bool bInherit);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);
}
