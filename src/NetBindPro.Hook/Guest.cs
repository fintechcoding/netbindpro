// Copyright (c) 2024 NetBind Pro Contributors
// Licensed under the MIT License. See LICENSE in the project root for details.

using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using EasyHook;
using NetBindPro.Hook.Native;

namespace NetBindPro.Hook;

/// <summary>
/// The payload injected into the target process via EasyHook.
/// Intercepts Winsock calls to force all outbound connections through
/// the specified network adapter's IP address.
/// </summary>
/// <remarks>
/// Key fix over the original NetworkAdapterSelector:
/// We do NOT filter on <c>SupportsMulticast</c>. WireGuard and Tailscale
/// interfaces typically do not set that flag, causing them to be silently
/// ignored by the original tool.
/// </remarks>
public sealed class Guest : IEntryPoint
{
    // ─── Fields ─────────────────────────────────────────────────────────────

    private readonly string _adapterId;
    private readonly string _injectorPath;
    private readonly int    _delayMs;
    private readonly bool   _changeTitle;
    private readonly bool   _killSwitch;
    private readonly string? _logPath;

    private readonly List<LocalHook> _hooks = [];
    private readonly Dictionary<IntPtr, bool> _boundSockets = [];
    // The address family each socket was created with, so we bind it to an adapter address
    // of the SAME family. Binding an IPv4 source address onto an IPv6 socket (or vice
    // versa) corrupts the socket and makes Chrome's network service die seconds in.
    private readonly Dictionary<IntPtr, AddressFamily> _socketFamily = [];
    private readonly object _socketsLock = new();

    // Child PIDs already scheduled for injection, so a spawn seen by both the
    // CreateProcessW/A hook and the CreateProcessInternalW hook is only injected once.
    private readonly HashSet<int> _injectedChildren = [];
    private readonly object _childrenLock = new();

    // ─── Winsock constants (kill-switch) ────────────────────────────────────
    private const int SOCKET_ERROR   = -1;
    private const int WSAENETUNREACH = 10051; // "Network is unreachable"

    // ─── Constructor ────────────────────────────────────────────────────────

    // ReSharper disable once UnusedParameter.Local
    public Guest(
        RemoteHooking.IContext context,
        string adapterId,
        string injectorPath,
        int delayMs,
        bool changeTitle,
        bool debug,
        bool killSwitch)
    {
        _adapterId    = adapterId;
        _injectorPath = injectorPath;
        _delayMs      = delayMs;
        _changeTitle  = changeTitle;
        _killSwitch   = killSwitch;

        if (debug)
        {
            try
            {
                var proc = System.Diagnostics.Process.GetCurrentProcess();
                _logPath = Path.Combine(
                    Path.GetTempPath(),
                    $"NetBindPro-{proc.ProcessName}[{proc.Id}].log");
            }
            catch { /* non-fatal */ }
        }

        Log("Guest", "Initialized — adapter: " + _adapterId);
    }

    // ─── IEntryPoint.Run ────────────────────────────────────────────────────

    public void Run(
        RemoteHooking.IContext context,
        string injectorPath,
        string adapterId,
        int delayMs,
        bool changeTitle,
        bool debug,
        bool killSwitch)
    {
        Log("Run", $"Installing hooks (kill-switch: {killSwitch})");

        // Pre-load ws2_32 so its exports are available at their final addresses
        var ws2Handle = NativeWinsock.LoadLibraryW("ws2_32.dll");

        InstallHook("ws2_32.dll", "socket",
            new SocketDelegate(OnSocket));

        InstallHook("ws2_32.dll", "WSASocketW",
            new WSASocketDelegate(OnWSASocket));

        InstallHook("ws2_32.dll", "connect",
            new ConnectDelegate(OnConnect));

        InstallHook("ws2_32.dll", "WSAConnect",
            new WSAConnectDelegate(OnWSAConnect));

        InstallHook("ws2_32.dll", "bind",
            new BindDelegate(OnBind));

        InstallHook("ws2_32.dll", "closesocket",
            new CloseSocketDelegate(OnClose));

        // Hook child process creation so spawned processes inherit the binding.
        // NOTE: we do NOT hook kernelbase!CreateProcessInternalW. It catches Chrome's
        // sandboxed renderer spawns at a point where injecting them destabilises the
        // browser (it exits moments after launch), and it did not help Firefox anyway
        // (whose launcher spawns the real browser via NtCreateUserProcess, below both).
        InstallHook("kernel32.dll", "CreateProcessW",
            new CreateProcessDelegate(CreateProcessW_Hook));

        InstallHook("kernel32.dll", "CreateProcessA",
            new CreateProcessDelegate(CreateProcessA_Hook));

        if (ws2Handle != IntPtr.Zero)
            NativeWinsock.FreeLibrary(ws2Handle);

        lock (_hooks)
        {
            if (_hooks.Count == 0)
            {
                Log("Run", "FATAL: No hooks installed — aborting");
                return;
            }
            Log("Run", $"{_hooks.Count} hooks installed — resuming process");
        }

        RemoteHooking.WakeUpProcess();

        // Keep alive: EasyHook requires Run() to remain active
        try
        {
            while (true)
                Thread.Sleep(1000);
        }
        catch (ThreadAbortException) { /* normal shutdown */ }
    }

    // ─── Hook: socket() ─────────────────────────────────────────────────────

    private IntPtr OnSocket(AddressFamily af, SocketType type, ProtocolType protocol)
    {
        var lastErr = NativeWinsock.WSAGetLastError();
        var s = NativeWinsock.socket((int)af, (int)type, (int)protocol);

        if (s != IntPtr.Zero)
        {
            lock (_socketsLock)
            {
                if (!_boundSockets.ContainsKey(s)) _boundSockets.Add(s, false);
                _socketFamily[s] = af;
            }
            Log("socket", $"New socket {s:X} af={af}");
        }

        NativeWinsock.WSASetLastError(lastErr);
        return s;
    }

    // ─── Hook: WSASocketW() ─────────────────────────────────────────────────

    private IntPtr OnWSASocket(AddressFamily af, SocketType type, ProtocolType protocol,
        IntPtr lpProtocolInfo, uint g, int dwFlags)
    {
        var lastErr = NativeWinsock.WSAGetLastError();

        // Call the REAL WSASocketW with the caller's original protocol info and flags.
        // Previously this substituted a plain socket(), which dropped WSA_FLAG_OVERLAPPED
        // and broke Chrome's network service (the browser exited a few seconds after launch).
        var s = NativeWinsock.WSASocketW((int)af, (int)type, (int)protocol, lpProtocolInfo, g, dwFlags);

        if (s != IntPtr.Zero && s != new IntPtr(-1))
            lock (_socketsLock)
            {
                if (!_boundSockets.ContainsKey(s)) _boundSockets.Add(s, false);
                _socketFamily[s] = af;
            }

        NativeWinsock.WSASetLastError(lastErr);
        return s;
    }

    // ─── Hook: bind() ───────────────────────────────────────────────────────

    private int OnBind(IntPtr s, IntPtr name, int namelen)
    {
        var lastErr = NativeWinsock.WSAGetLastError();
        var destFam = ReadAddressFamily(name);

        // Let internal/loopback binds pass through unchanged
        if (ShouldSkip(name, destFam))
        {
            NativeWinsock.WSASetLastError(lastErr);
            return NativeWinsock.bind(s, name, namelen);
        }

        AddressFamily sockFam;
        lock (_socketsLock) _socketFamily.TryGetValue(s, out sockFam);
        var fam = sockFam is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6 ? sockFam : destFam;

        var port = ReadPort(name);
        var result = TryBindToAdapter(s, fam, port);

        if (result == 0)
        {
            // Mark bound so the later connect() hook won't try to re-bind (which would
            // fail with WSAEINVAL and wrongly trip the kill-switch on a valid socket).
            lock (_socketsLock) { _boundSockets[s] = true; }
            Log("bind", $"Socket {s:X} rebound to adapter on port {port}");
        }
        else
        {
            Log("bind", $"Socket {s:X} rebind failed, falling through");
        }

        NativeWinsock.WSASetLastError(lastErr);
        return result != 0 ? NativeWinsock.bind(s, name, namelen) : result;
    }

    // ─── Hook: connect() ────────────────────────────────────────────────────

    private int OnConnect(IntPtr s, IntPtr name, int namelen)
    {
        if (!EnsureBound(s, name) && _killSwitch)
        {
            // Kill-switch ON: the target adapter is down / has no usable IP.
            // Refuse the connection instead of leaking it out the default route.
            NativeWinsock.WSASetLastError(WSAENETUNREACH);
            return SOCKET_ERROR;
        }
        return NativeWinsock.connect(s, name, namelen);
    }

    // ─── Hook: WSAConnect() ─────────────────────────────────────────────────

    private int OnWSAConnect(IntPtr s, IntPtr name, int namelen,
        IntPtr lpCallerData, IntPtr lpCalleeData, IntPtr lpSQOS, IntPtr lpGQOS)
    {
        if (!EnsureBound(s, name) && _killSwitch)
        {
            NativeWinsock.WSASetLastError(WSAENETUNREACH);
            return SOCKET_ERROR;
        }
        return NativeWinsock.WSAConnect(s, name, namelen, lpCallerData, lpCalleeData, lpSQOS, lpGQOS);
    }

    // ─── Hook: closesocket() ────────────────────────────────────────────────

    private int OnClose(IntPtr s)
    {
        var result = NativeWinsock.closesocket(s);
        if (result == 0)
            lock (_socketsLock) { _boundSockets.Remove(s); _socketFamily.Remove(s); }
        return result;
    }

    // ─── Hook: CreateProcess() ──────────────────────────────────────────────

    private bool CreateProcessW_Hook(
        IntPtr lpApp, IntPtr lpCmd, IntPtr lpPA, IntPtr lpTA,
        bool bInherit, uint dwFlags, IntPtr lpEnv, IntPtr lpDir,
        IntPtr lpSI, out ProcessInformation pi)
        => OnCreateProcess(lpApp, lpCmd, lpPA, lpTA, bInherit, dwFlags, lpEnv, lpDir, lpSI, out pi, unicode: true);

    private bool CreateProcessA_Hook(
        IntPtr lpApp, IntPtr lpCmd, IntPtr lpPA, IntPtr lpTA,
        bool bInherit, uint dwFlags, IntPtr lpEnv, IntPtr lpDir,
        IntPtr lpSI, out ProcessInformation pi)
        => OnCreateProcess(lpApp, lpCmd, lpPA, lpTA, bInherit, dwFlags, lpEnv, lpDir, lpSI, out pi, unicode: false);

    private bool OnCreateProcess(
        IntPtr lpApp, IntPtr lpCmd, IntPtr lpPA, IntPtr lpTA,
        bool bInherit, uint dwFlags, IntPtr lpEnv, IntPtr lpDir,
        IntPtr lpSI, out ProcessInformation pi, bool unicode)
    {
        bool result = unicode
            ? NativeKernel.CreateProcessW(lpApp, lpCmd, lpPA, lpTA, bInherit, dwFlags, lpEnv, lpDir, lpSI, out pi)
            : NativeKernel.CreateProcessA(lpApp, lpCmd, lpPA, lpTA, bInherit, dwFlags, lpEnv, lpDir, lpSI, out pi);

        if (result && pi.dwProcessId > 0)
        {
            string? cmd = lpCmd == IntPtr.Zero
                ? null
                : (unicode ? Marshal.PtrToStringUni(lpCmd) : Marshal.PtrToStringAnsi(lpCmd));

            // Don't inject sandboxed browser children (renderers/GPU/crashpad): they enable
            // Arbitrary Code Guard, so injecting them fails and can destabilise the browser.
            // The main and network/utility processes (where traffic flows) are not skipped.
            if (IsSandboxedSpawn(cmd))
                Log("CreateProcess", $"Skipping sandboxed child PID {pi.dwProcessId}");
            else
                ScheduleChildInjection(pi.dwProcessId);
        }

        return result;
    }

    /// <summary>True for a sandboxed browser child that must not be injected.</summary>
    private static bool IsSandboxedSpawn(string? commandLine)
    {
        if (string.IsNullOrEmpty(commandLine)) return false;
        if (commandLine.Contains("--type=renderer")) return true;
        if (commandLine.Contains("--type=gpu-process")) return true;
        if (commandLine.Contains("--type=crashpad-handler")) return true;
        if (commandLine.Contains("--type=ppapi")) return true;
        // Firefox content tab processes (keep the main + socket/network process injectable)
        if (commandLine.Contains("-contentproc") &&
            (commandLine.TrimEnd().EndsWith(" tab", StringComparison.OrdinalIgnoreCase) ||
             commandLine.Contains(" tab ")))
            return true;
        return false;
    }

    /// <summary>
    /// Injects the binding into a freshly-spawned child process, once. De-duplicated so a
    /// spawn observed by both the CreateProcessW/A and CreateProcessInternalW hooks only
    /// triggers a single injection. Runs asynchronously so the parent is never blocked.
    /// </summary>
    private void ScheduleChildInjection(int childPid)
    {
        lock (_childrenLock)
        {
            if (!_injectedChildren.Add(childPid)) return; // already handled
        }

        var injPath    = _injectorPath;
        var aid        = _adapterId;
        var delay      = _delayMs;
        var logEnabled = _logPath != null;
        var ks         = _killSwitch;

        new Thread(() =>
        {
            Thread.Sleep(Math.Max(delay, 800));
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    RemoteHooking.Inject(
                        childPid, injPath, injPath,
                        aid, injPath, delay, false, logEnabled, ks);
                    Log("ChildInject", $"Child PID {childPid} injected successfully");
                    return;
                }
                catch (Exception ex)
                {
                    Log("ChildInject", $"Child {childPid} inject attempt {attempt + 1}/3 failed: {ex.Message}");
                    Thread.Sleep(1000);
                }
            }
        })
        { IsBackground = true }.Start();
    }

    // ─── Binding logic ───────────────────────────────────────────────────────

    /// <summary>
    /// Ensures this socket is bound to the adapter's IP address before it connects.
    /// Returns <c>true</c> when the socket is safe to connect (bound to the adapter,
    /// or a destination that is intentionally left alone such as loopback/multicast),
    /// and <c>false</c> when binding failed — in which case the caller must BLOCK the
    /// connection rather than let it leak out the default route (kill-switch).
    /// Called just before connect() / WSAConnect().
    /// </summary>
    private bool EnsureBound(IntPtr s, IntPtr name)
    {
        bool alreadyBound;
        AddressFamily sockFam;
        lock (_socketsLock)
        {
            alreadyBound = _boundSockets.TryGetValue(s, out var v) && v;
            _socketFamily.TryGetValue(s, out sockFam);
        }

        if (alreadyBound) return true;

        var destFam = ReadAddressFamily(name);
        if (ShouldSkip(name, destFam)) return true; // loopback / multicast / link-local pass through

        // Bind using the SOCKET's own family (fallback to the destination's), so an
        // IPv6/dual-stack socket is never bound with an IPv4 sockaddr (which corrupts it).
        var fam = sockFam is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6 ? sockFam : destFam;

        var result = TryBindToAdapter(s, fam, port: 0);

        lock (_socketsLock)
            _boundSockets[s] = result == 0;

        if (result == 0)
        {
            Log("EnsureBound", $"Socket {s:X} auto-bound to adapter");
            return true;
        }

        Log("EnsureBound", $"Socket {s:X} bind FAILED (adapter down / no IP) — BLOCKING to prevent leak");
        return false;
    }

    /// <summary>
    /// Binds <paramref name="s"/> to the adapter's IP address on port 0 (OS assigns port).
    /// Returns 0 on success, socket error code otherwise.
    /// </summary>
    private int TryBindToAdapter(IntPtr s, AddressFamily fam, ushort port)
    {
        if (!TryGetAdapterAddresses(fam, out var ip4, out var ip6))
        {
            Log("TryBindToAdapter", $"Adapter {_adapterId} not found or has no IP for {fam}");
            return -1;
        }

        // CRITICAL: bind with a sockaddr of the SOCKET's family. Using an IPv4 sockaddr on
        // an IPv6 socket (or vice-versa) corrupts the socket and makes Chrome's network
        // service die seconds after launch.
        if (fam == AddressFamily.InterNetworkV6)
        {
            if (ip6 != null) return BindV6(s, ip6, port);
            // Dual-stack socket on an adapter with no usable IPv6: bind the socket to the
            // adapter's IPv4 as an IPv4-mapped IPv6 address (::ffff:a.b.c.d). This routes the
            // socket's (IPv4) traffic through the adapter without a family mismatch.
            if (ip4 != null) return BindV6(s, ip4.MapToIPv6(), port);
            return -1;
        }

        if (ip4 != null)
            return BindV4(s, ip4, port);

        return -1;
    }

    private int BindV4(IntPtr s, IPAddress ip, ushort port)
    {
        var bytes = ip.GetAddressBytes();
        var addr = new SockAddrIn
        {
            sin_family = (short)AddressFamily.InterNetwork,
            sin_port   = HostToNetworkPort(port),
            sin_addr   = BitConverter.ToUInt32(bytes, 0),
            sin_zero   = new byte[8]
        };

        Log("BindV4", $"Binding to {ip}:{port}");
        return NativeWinsock.bind(s, ref addr, Marshal.SizeOf<SockAddrIn>());
    }

    private int BindV6(IntPtr s, IPAddress ip, ushort port)
    {
        var addr = new SockAddrIn6
        {
            sin6_family   = (short)AddressFamily.InterNetworkV6,
            sin6_port     = HostToNetworkPort(port),
            sin6_flowinfo = 0,
            sin6_addr     = ip.GetAddressBytes(),
            sin6_scope_id = 0
        };

        Log("BindV6", $"Binding to [{ip}]:{port}");
        return NativeWinsock.bind6(s, ref addr, Marshal.SizeOf<SockAddrIn6>());
    }

    // ─── Adapter resolution ──────────────────────────────────────────────────

    /// <summary>
    /// Resolves the adapter's current IP addresses.
    /// KEY FIX: No <c>SupportsMulticast</c> filter — WireGuard and Tailscale
    /// do not set that flag, but must still be bindable.
    /// </summary>
    private bool TryGetAdapterAddresses(AddressFamily preferredFamily,
        out IPAddress? ip4, out IPAddress? ip6)
    {
        ip4 = null;
        ip6 = null;

        var wantedId = _adapterId.Trim('{', '}');
        var nic = NetworkInterface.GetAllNetworkInterfaces()
            .FirstOrDefault(n =>
                n.Id.Trim('{', '}').Equals(wantedId, StringComparison.OrdinalIgnoreCase) &&
                n.OperationalStatus == OperationalStatus.Up
                /* No SupportsMulticast check — intentional! */);

        if (nic is null) return false;

        try
        {
            var props = nic.GetIPProperties();

            ip4 = props.UnicastAddresses
                .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                ?.Address;

            ip6 = props.UnicastAddresses
                .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6
                                  && !a.Address.IsIPv6LinkLocal)
                ?.Address;
        }
        catch { return false; }

        return ip4 != null || ip6 != null;
    }

    // ─── Address helpers ─────────────────────────────────────────────────────

    private static AddressFamily ReadAddressFamily(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero) return AddressFamily.Unknown;
        try
        {
            var b = Marshal.PtrToStructure<SockAddrBase>(ptr);
            return (AddressFamily)b.sa_family;
        }
        catch { return AddressFamily.Unknown; }
    }

    private static ushort ReadPort(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero) return 0;
        try
        {
            var s = Marshal.PtrToStructure<SockAddrIn>(ptr);
            // sin_port is big-endian; convert back to host order
            return (ushort)((s.sin_port << 8) | (s.sin_port >> 8));
        }
        catch { return 0; }
    }

    /// <summary>
    /// Returns true for addresses that should NOT be re-routed through the adapter.
    /// Loopback, link-local, and multicast must pass through unchanged.
    /// Note: the Tailscale CGNAT range (100.64.0.0/10) is intentionally NOT skipped
    /// so that Tailscale traffic can be forced through a specific adapter if desired.
    /// </summary>
    private static bool ShouldSkip(IntPtr ptr, AddressFamily fam)
    {
        if (ptr == IntPtr.Zero) return true;

        switch (fam)
        {
            case AddressFamily.InterNetwork:
            {
                var s4 = Marshal.PtrToStructure<SockAddrIn>(ptr);
                var ip = new IPAddress(s4.sin_addr);
                if (IPAddress.IsLoopback(ip)) return true;
                if (ip.Equals(IPAddress.Any)) return true;
                // Multicast 224.0.0.0/4
                var b0 = s4.sin_addr & 0xFF;
                if (b0 >= 224 && b0 <= 239) return true;
                // Link-local 169.254.0.0/16
                var b1 = (s4.sin_addr >> 8) & 0xFF;
                if (b0 == 169 && b1 == 254) return true;
                return false;
            }
            case AddressFamily.InterNetworkV6:
            {
                var s6 = Marshal.PtrToStructure<SockAddrIn6>(ptr);
                var ip = new IPAddress(s6.sin6_addr);
                return IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.IPv6Any) || ip.IsIPv6LinkLocal;
            }
            default:
                return true; // skip unknown families
        }
    }

    // ─── Utilities ───────────────────────────────────────────────────────────

    private static ushort HostToNetworkPort(ushort port)
        => (ushort)((port << 8) | (port >> 8));

    private void InstallHook(string library, string entryPoint, Delegate handler)
    {
        try
        {
            var hook = LocalHook.Create(
                LocalHook.GetProcAddress(library, entryPoint),
                handler,
                null);

            // ACL: exclude EasyHook's own thread (thread 0 = current)
            hook.ThreadACL.SetExclusiveACL([0]);

            lock (_hooks) { _hooks.Add(hook); }
            Log("Hook", $"✓ {entryPoint} @ {library}");
        }
        catch (Exception ex)
        {
            Log("Hook", $"✗ {entryPoint} @ {library}: {ex.Message}");
        }
    }

    private void Log(string scope, string message)
    {
        if (_logPath is null) return;
        try
        {
            File.AppendAllText(
                _logPath,
                $"{DateTime.UtcNow:HH:mm:ss.fff} [{scope,-20}] {message}{Environment.NewLine}");
        }
        catch { /* best-effort logging */ }
    }

    // ─── Destructor ──────────────────────────────────────────────────────────

    ~Guest()
    {
        lock (_hooks)
        {
            foreach (var hook in _hooks)
            {
                try { hook.Dispose(); }
                catch { /* ignore */ }
            }
            _hooks.Clear();
        }
    }
}
