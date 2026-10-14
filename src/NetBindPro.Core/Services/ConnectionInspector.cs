// Copyright (c) 2024 NetBind Pro Contributors
// Licensed under the MIT License. See LICENSE in the project root for details.

using System.Net;
using System.Runtime.InteropServices;

namespace NetBindPro.Core.Services;

/// <summary>
/// Inspects a process's live TCP connections so a rule can be VERIFIED: a bound process's
/// outbound connections must originate from the chosen adapter's IP (otherwise it is still
/// leaking out the default route). Uses the IP Helper API (<c>GetExtendedTcpTable</c>).
/// </summary>
public static class ConnectionInspector
{
    /// <summary>
    /// Returns the distinct local IPv4 addresses that <paramref name="pid"/> currently has
    /// outbound (non-loopback, connected/connecting) TCP connections on. For a correctly
    /// bound process these are all the adapter's IP.
    /// </summary>
    public static IReadOnlyList<string> GetOutboundLocalAddresses(int pid)
    {
        var result = new List<string>();
        int size = 0;
        _ = GetExtendedTcpTable(IntPtr.Zero, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
        if (size <= 0) return result;

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buffer, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0) != 0)
                return result;

            var rowCount = Marshal.ReadInt32(buffer);
            var rowPtr = IntPtr.Add(buffer, 4);
            var rowSize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();

            for (var i = 0; i < rowCount; i++)
            {
                var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(IntPtr.Add(rowPtr, i * rowSize));
                if (row.owningPid != pid) continue;

                // Only outbound connections that have a real remote peer (skip listeners and
                // loopback). States: 2=LISTEN skip; others with a remote addr count.
                var remote = new IPAddress(BitConverter.GetBytes(row.remoteAddr));
                if (IPAddress.IsLoopback(remote) || remote.Equals(IPAddress.Any)) continue;

                var local = new IPAddress(BitConverter.GetBytes(row.localAddr)).ToString();
                if (local != "0.0.0.0" && !result.Contains(local))
                    result.Add(local);
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }

        return result;
    }

    // ─── IP Helper interop ────────────────────────────────────────────────────

    private const int AF_INET = 2;
    private const int TCP_TABLE_OWNER_PID_ALL = 5;

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCPROW_OWNER_PID
    {
        public uint state;
        public uint localAddr;
        public uint localPort;
        public uint remoteAddr;
        public uint remotePort;
        public uint owningPid;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr pTcpTable, ref int pdwSize, bool bOrder, int ulAf, int tableClass, int reserved);
}
