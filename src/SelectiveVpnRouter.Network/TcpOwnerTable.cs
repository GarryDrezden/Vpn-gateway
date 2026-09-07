using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace SelectiveVpnRouter.Network;

public sealed record TcpOwner(IPEndPoint Local, IPEndPoint Remote, int Pid, string State);

public static class TcpOwnerTable
{
    public static IReadOnlyList<TcpOwner> Snapshot()
    {
        uint size = 0;
        uint st = Native.GetExtendedTcpTable(IntPtr.Zero, ref size, true, 2, Native.TcpTableOwnerPidAll, 0);
        if (st != 122 && st != 0)
        {
            return [];
        }

        IntPtr buf = Marshal.AllocHGlobal((int)size);
        try
        {
            st = Native.GetExtendedTcpTable(buf, ref size, true, 2, Native.TcpTableOwnerPidAll, 0);
            if (st != 0)
            {
                return [];
            }

            int count = Marshal.ReadInt32(buf);
            int offset = 4;
            var list = new List<TcpOwner>(count);
            for (int i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<Native.MibTcpRowOwnerPid>(buf + offset)!;
                offset += Marshal.SizeOf<Native.MibTcpRowOwnerPid>();
                var local = new IPEndPoint(new IPAddress(BitConverter.GetBytes(row.LocalAddr)), Native.Ntohs(row.LocalPort));
                var remote = new IPEndPoint(new IPAddress(BitConverter.GetBytes(row.RemoteAddr)), Native.Ntohs(row.RemotePort));
                list.Add(new TcpOwner(local, remote, (int)row.OwningPid, row.State.ToString()));
            }

            return list;
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    public static int? PidForLocal(IPEndPoint local)
        => Snapshot().FirstOrDefault(r => r.Local.Equals(local))?.Pid;

    private static class Native
    {
        public const int TcpTableOwnerPidAll = 5;

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

        [DllImport("iphlpapi.dll", SetLastError = true)]
        public static extern uint GetExtendedTcpTable(IntPtr table, ref uint size, bool order, int af, int tableClass, uint reserved);

        public static int Ntohs(uint packed)
        {
            ushort n = (ushort)(packed & 0xFFFF);
            return (ushort)IPAddress.NetworkToHostOrder((short)n);
        }
    }
}

public static class ProcessPathResolver
{
    public static string? TryGetPath(int pid)
    {
        if (pid <= 0)
        {
            return null;
        }

        try
        {
            using var p = System.Diagnostics.Process.GetProcessById(pid);
            return p.MainModule?.FileName;
        }
        catch (Exception)
        {
            return TryQueryFullProcessImageName(pid);
        }
    }

    private static string? TryQueryFullProcessImageName(int pid)
    {
        IntPtr h = Native.OpenProcess(0x1000, false, pid);
        if (h == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var sb = new System.Text.StringBuilder(1024);
            int size = sb.Capacity;
            return Native.QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }

    private static class Native
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool QueryFullProcessImageName(IntPtr h, int flags, System.Text.StringBuilder name, ref int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr h);
    }
}
