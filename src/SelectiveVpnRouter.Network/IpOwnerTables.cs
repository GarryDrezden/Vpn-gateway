using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using SelectiveVpnRouter.Core.RoutingTrace;

namespace SelectiveVpnRouter.Network;

public static class IpOwnerTables
{
    public static IReadOnlyList<PassiveSocketObservation> SnapshotTcp(RoutingTraceAddressFamily family)
        => family == RoutingTraceAddressFamily.IPv6 ? SnapshotTcp6() : SnapshotTcp4();

    public static IReadOnlyList<PassiveSocketObservation> SnapshotUdp(RoutingTraceAddressFamily family)
        => family == RoutingTraceAddressFamily.IPv6 ? SnapshotUdp6() : SnapshotUdp4();

    private static IReadOnlyList<PassiveSocketObservation> SnapshotTcp4()
    {
        return SnapshotTable(
            af: 2,
            tableClass: Native.TcpTableOwnerPidAll,
            rowSize: Marshal.SizeOf<Native.MibTcpRowOwnerPid>(),
            parse: (buf, offset) =>
            {
                var row = Marshal.PtrToStructure<Native.MibTcpRowOwnerPid>(buf + offset)!;
                return new PassiveSocketObservation(
                    (int)row.OwningPid,
                    RoutingTraceProtocol.Tcp,
                    RoutingTraceAddressFamily.IPv4,
                    new IPAddress(BitConverter.GetBytes(row.LocalAddr)).ToString(),
                    Native.Ntohs(row.LocalPort),
                    new IPAddress(BitConverter.GetBytes(row.RemoteAddr)).ToString(),
                    Native.Ntohs(row.RemotePort),
                    row.State.ToString(),
                    DateTimeOffset.UtcNow);
            });
    }

    private static IReadOnlyList<PassiveSocketObservation> SnapshotTcp6()
    {
        int rowSize = Marshal.SizeOf<Native.MibTcp6RowOwnerPid>();
        return SnapshotTable(
            af: 23,
            tableClass: Native.TcpTableOwnerPidAll,
            rowSize: rowSize,
            parse: (buf, offset) =>
            {
                var row = Marshal.PtrToStructure<Native.MibTcp6RowOwnerPid>(buf + offset)!;
                var local = new IPAddress(row.LocalAddr, row.LocalScopeId);
                var remote = new IPAddress(row.RemoteAddr, row.RemoteScopeId);
                return new PassiveSocketObservation(
                    (int)row.OwningPid,
                    RoutingTraceProtocol.Tcp,
                    RoutingTraceAddressFamily.IPv6,
                    local.ToString(),
                    Native.Ntohs(row.LocalPort),
                    remote.ToString(),
                    Native.Ntohs(row.RemotePort),
                    row.State.ToString(),
                    DateTimeOffset.UtcNow);
            });
    }

    private static IReadOnlyList<PassiveSocketObservation> SnapshotUdp4()
    {
        return SnapshotTable(
            af: 2,
            tableClass: Native.UdpTableOwnerPid,
            rowSize: Marshal.SizeOf<Native.MibUdpRowOwnerPid>(),
            parse: (buf, offset) =>
            {
                var row = Marshal.PtrToStructure<Native.MibUdpRowOwnerPid>(buf + offset)!;
                return new PassiveSocketObservation(
                    (int)row.OwningPid,
                    RoutingTraceProtocol.Udp,
                    RoutingTraceAddressFamily.IPv4,
                    new IPAddress(BitConverter.GetBytes(row.LocalAddr)).ToString(),
                    Native.Ntohs(row.LocalPort),
                    "0.0.0.0",
                    0,
                    "UDP",
                    DateTimeOffset.UtcNow);
            });
    }

    private static IReadOnlyList<PassiveSocketObservation> SnapshotUdp6()
    {
        int rowSize = Marshal.SizeOf<Native.MibUdp6RowOwnerPid>();
        return SnapshotTable(
            af: 23,
            tableClass: Native.UdpTableOwnerPid,
            rowSize: rowSize,
            parse: (buf, offset) =>
            {
                var row = Marshal.PtrToStructure<Native.MibUdp6RowOwnerPid>(buf + offset)!;
                var local = new IPAddress(row.LocalAddr, row.LocalScopeId);
                return new PassiveSocketObservation(
                    (int)row.OwningPid,
                    RoutingTraceProtocol.Udp,
                    RoutingTraceAddressFamily.IPv6,
                    local.ToString(),
                    Native.Ntohs(row.LocalPort),
                    "::",
                    0,
                    "UDP",
                    DateTimeOffset.UtcNow);
            });
    }

    private static List<PassiveSocketObservation> SnapshotTable(
        int af,
        int tableClass,
        int rowSize,
        Func<IntPtr, int, PassiveSocketObservation> parse)
    {
        uint size = 0;
        uint st = tableClass == Native.UdpTableOwnerPid
            ? Native.GetExtendedUdpTable(IntPtr.Zero, ref size, true, af, tableClass, 0)
            : Native.GetExtendedTcpTable(IntPtr.Zero, ref size, true, af, tableClass, 0);

        if (st != 122 && st != 0)
        {
            return [];
        }

        IntPtr buf = Marshal.AllocHGlobal((int)size);
        try
        {
            st = tableClass == Native.UdpTableOwnerPid
                ? Native.GetExtendedUdpTable(buf, ref size, true, af, tableClass, 0)
                : Native.GetExtendedTcpTable(buf, ref size, true, af, tableClass, 0);
            if (st != 0)
            {
                return [];
            }

            int count = Marshal.ReadInt32(buf);
            int offset = 4;
            var list = new List<PassiveSocketObservation>(count);
            for (int i = 0; i < count; i++)
            {
                list.Add(parse(buf, offset));
                offset += rowSize;
            }

            return list;
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    private static class Native
    {
        public const int TcpTableOwnerPidAll = 5;
        public const int UdpTableOwnerPid = 1;

        [StructLayout(LayoutKind.Sequential)]
        public struct MibTcpRowOwnerPid
        {
            public uint State;
            public uint LocalAddr;
            public uint LocalPort;
            public uint RemoteAddr;
            public uint RemotePort;
            public uint OwningPid;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MibTcp6RowOwnerPid
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            public byte[] LocalAddr;
            public uint LocalScopeId;
            public uint LocalPort;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            public byte[] RemoteAddr;
            public uint RemoteScopeId;
            public uint RemotePort;
            public uint State;
            public uint OwningPid;

            public MibTcp6RowOwnerPid()
            {
                LocalAddr = new byte[16];
                RemoteAddr = new byte[16];
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MibUdpRowOwnerPid
        {
            public uint LocalAddr;
            public uint LocalPort;
            public uint OwningPid;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MibUdp6RowOwnerPid
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            public byte[] LocalAddr;
            public uint LocalScopeId;
            public uint LocalPort;
            public uint OwningPid;

            public MibUdp6RowOwnerPid()
            {
                LocalAddr = new byte[16];
            }
        }

        [DllImport("iphlpapi.dll", SetLastError = true)]
        public static extern uint GetExtendedTcpTable(IntPtr table, ref uint size, bool order, int af, int tableClass, uint reserved);

        [DllImport("iphlpapi.dll", SetLastError = true)]
        public static extern uint GetExtendedUdpTable(IntPtr table, ref uint size, bool order, int af, int tableClass, uint reserved);

        public static int Ntohs(uint packed)
        {
            ushort n = (ushort)(packed & 0xFFFF);
            return (ushort)IPAddress.NetworkToHostOrder((short)n);
        }
    }
}
