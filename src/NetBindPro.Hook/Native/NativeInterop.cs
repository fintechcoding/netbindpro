// Copyright (c) 2024 NetBind Pro Contributors
// Licensed under the MIT License. See LICENSE in the project root for details.

using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace NetBindPro.Hook.Native;

// ─── Winsock socket address structures ───────────────────────────────────────

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct SockAddrBase
{
    public short sa_family;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct SockAddrIn
{
    public short  sin_family;
    public ushort sin_port;       // network byte order (big-endian)
    public uint   sin_addr;       // IPv4 address in network byte order
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
    public byte[] sin_zero;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct SockAddrIn6
{
    public short  sin6_family;
    public ushort sin6_port;
    public uint   sin6_flowinfo;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
    public byte[] sin6_addr;
    public uint   sin6_scope_id;
}

// ─── Process info for CreateProcess ──────────────────────────────────────────

[StructLayout(LayoutKind.Sequential)]
internal struct ProcessInformation
{
    public IntPtr hProcess;
    public IntPtr hThread;
    public int    dwProcessId;
    public int    dwThreadId;
}

// ─── Delegate signatures for hooked functions ────────────────────────────────

internal delegate IntPtr SocketDelegate(
    AddressFamily af, SocketType type, ProtocolType protocol);

internal delegate IntPtr WSASocketDelegate(
    AddressFamily af, SocketType type, ProtocolType protocol,
    IntPtr lpProtocolInfo, uint g, int dwFlags);

internal delegate int BindDelegate(IntPtr s, IntPtr name, int namelen);

internal delegate int ConnectDelegate(IntPtr s, IntPtr name, int namelen);

internal delegate int WSAConnectDelegate(
    IntPtr s, IntPtr name, int namelen,
    IntPtr lpCallerData, IntPtr lpCalleeData, IntPtr lpSQOS, IntPtr lpGQOS);

internal delegate int CloseSocketDelegate(IntPtr s);

internal delegate bool CreateProcessDelegate(
    IntPtr lpApplicationName, IntPtr lpCommandLine,
    IntPtr lpProcessAttributes, IntPtr lpThreadAttributes,
    bool bInheritHandles, uint dwCreationFlags,
    IntPtr lpEnvironment, IntPtr lpCurrentDirectory,
    IntPtr lpStartupInfo, out ProcessInformation lpProcessInformation);

// ─── P/Invoke declarations ────────────────────────────────────────────────────

internal static partial class NativeWinsock
{
    [LibraryImport("ws2_32.dll", SetLastError = true)]
    public static partial IntPtr socket(int af, int type, int protocol);

    [DllImport("ws2_32.dll", EntryPoint = "bind", SetLastError = true)]
    public static extern int bind(IntPtr s, IntPtr name, int namelen);

    [DllImport("ws2_32.dll", EntryPoint = "bind", SetLastError = true)]
    public static extern int bind(IntPtr s, ref SockAddrIn addr, int namelen);

    [DllImport("ws2_32.dll", EntryPoint = "bind", SetLastError = true)]
    public static extern int bind6(IntPtr s, ref SockAddrIn6 addr, int namelen);

    [LibraryImport("ws2_32.dll", SetLastError = true)]
    public static partial int connect(IntPtr s, IntPtr name, int namelen);

    [LibraryImport("ws2_32.dll", SetLastError = true)]
    public static partial int WSAConnect(
        IntPtr s, IntPtr name, int namelen,
        IntPtr lpCallerData, IntPtr lpCalleeData, IntPtr lpSQOS, IntPtr lpGQOS);

    [LibraryImport("ws2_32.dll", SetLastError = true)]
    public static partial int closesocket(IntPtr s);

    [LibraryImport("ws2_32.dll")]
    public static partial int WSAGetLastError();

    [LibraryImport("ws2_32.dll")]
    public static partial void WSASetLastError(int iError);

    [LibraryImport("kernel32.dll")]
    public static partial IntPtr LoadLibraryW([MarshalAs(UnmanagedType.LPWStr)] string lpLibFileName);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool FreeLibrary(IntPtr hModule);
}

internal static partial class NativeKernel
{
    [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool CreateProcessW(
        IntPtr lpApplicationName, IntPtr lpCommandLine,
        IntPtr lpProcessAttributes, IntPtr lpThreadAttributes,
        bool bInheritHandles, uint dwCreationFlags,
        IntPtr lpEnvironment, IntPtr lpCurrentDirectory,
        IntPtr lpStartupInfo, out ProcessInformation lpProcessInformation);

    [DllImport("kernel32.dll", EntryPoint = "CreateProcessA", SetLastError = true, CharSet = CharSet.Ansi)]
    public static extern bool CreateProcessA(
        IntPtr lpApplicationName, IntPtr lpCommandLine,
        IntPtr lpProcessAttributes, IntPtr lpThreadAttributes,
        bool bInheritHandles, uint dwCreationFlags,
        IntPtr lpEnvironment, IntPtr lpCurrentDirectory,
        IntPtr lpStartupInfo, out ProcessInformation lpProcessInformation);
}
