using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace SelectiveVpn.V0.Network;

/// <summary>
/// Locale-independent snapshot via .NET NetworkInterface plus IP Helper GetIpForwardTable.
/// V0.1 may delete one exact TEST_IP/32 in emergency cleanup only, and only if that
/// host route still matches the row observed after our OpenVPN --route.
/// </summary>
internal static class NetworkInspector
{
    private const uint ErrorInsufficientBuffer = 122;
    private const uint NoError = 0;
    private const uint MibIpProtoNetMgmt = 3;

    public static NetworkSnapshot Capture()
    {
        IReadOnlyList<AdapterInfo> adapters = CaptureAdapters();
        IReadOnlyList<DefaultRouteEntry> defaults = CaptureIPv4DefaultRoutes(adapters);
        IReadOnlyList<DnsBinding> dns = adapters
            .Select(a => new DnsBinding
            {
                AdapterId = a.Id,
                AdapterName = a.Name,
                Servers = CaptureDns(a.Id),
            })
            .ToList();

        return new NetworkSnapshot
        {
            TakenAtUtc = DateTime.UtcNow,
            IPv4DefaultRoutes = defaults,
            Adapters = adapters,
            Dns = dns,
        };
    }

    public static Ipv4RouteEntry? FindHostRoute(IPAddress destination, int? requiredInterfaceIndex = null)
    {
        IReadOnlyList<AdapterInfo> adapters = CaptureAdapters();
        IEnumerable<Ipv4RouteEntry> matches = ReadAllIPv4Routes(adapters)
            .Where(r => r.MatchesHost(destination));
        if (requiredInterfaceIndex is int idx)
        {
            matches = matches.Where(r => r.InterfaceIndex == idx);
        }

        Ipv4RouteEntry[] list = matches.ToArray();
        return list.Length == 1 ? list[0] : list.FirstOrDefault();
    }

    public static IReadOnlyList<Ipv4RouteEntry> FindInternetWideRoutes()
    {
        IReadOnlyList<AdapterInfo> adapters = CaptureAdapters();
        return ReadAllIPv4Routes(adapters)
            .Where(r => r.IsDefault || r.IsSplitDefaultHalf)
            .ToList();
    }

    public static IReadOnlyList<string> DiffNewInternetWideRoutes(
        IReadOnlyList<Ipv4RouteEntry> before,
        IReadOnlyList<Ipv4RouteEntry> after)
    {
        var notes = new List<string>();
        foreach (Ipv4RouteEntry route in after)
        {
            bool existed = before.Any(b =>
                b.Destination.Equals(route.Destination)
                && b.Mask.Equals(route.Mask)
                && b.InterfaceIndex == route.InterfaceIndex
                && b.NextHop.Equals(route.NextHop));
            if (!existed)
            {
                notes.Add("Unexpected Internet-wide route: " + route);
            }
        }

        return notes;
    }

    /// <summary>
    /// Emergency-only: delete one IPv4 host route that still matches dest/mask/ifIndex/nextHop
    /// observed from our OpenVPN --route. Never deletes default or /1 split-tunnel routes.
    /// </summary>
    public static bool TryDeleteExactHostRoute(Ipv4RouteEntry expected, out string message)
    {
        if (!expected.MatchesHost(expected.Destination) || expected.IsDefault || expected.IsSplitDefaultHalf)
        {
            message = "Refusing to delete: route is not a host /32 or looks Internet-wide.";
            return false;
        }

        IReadOnlyList<AdapterInfo> adapters = CaptureAdapters();
        List<IpForwardRow> rows = NativeIpForwardTable.ReadIPv4();
        uint dest = ToNetworkOrderDword(expected.Destination);
        uint mask = ToNetworkOrderDword(expected.Mask);
        uint nextHop = ToNetworkOrderDword(expected.NextHop);
        IpForwardRow[] matches = rows
            .Where(r =>
                r.ForwardDest == dest
                && r.ForwardMask == mask
                && r.ForwardIfIndex == (uint)expected.InterfaceIndex
                && r.ForwardNextHop == nextHop)
            .ToArray();

        if (matches.Length != 1)
        {
            message = matches.Length == 0
                ? "Exact /32 row is no longer in the routing table."
                : $"Refusing to delete: {matches.Length} matching /32 rows, not unique.";
            return false;
        }

        IpForwardRow row = matches[0];
        row.ForwardProto = MibIpProtoNetMgmt;
        uint status = NativeIpForwardTable.Delete(ref row);
        if (status != NoError)
        {
            message = $"DeleteIpForwardEntry failed: {status}";
            return false;
        }

        message = "Deleted the exact TEST_IP/32 that matched the V0.1 observed row.";
        return true;
    }

    public static IReadOnlyList<AdapterInfo> CaptureAdapters()
    {
        var list = new List<AdapterInfo>();
        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            IPInterfaceProperties props = nic.GetIPProperties();
            int? ipv4Index = TryIndex(() => props.GetIPv4Properties().Index);
            int? ipv6Index = TryIndex(() => props.GetIPv6Properties().Index);

            var ipv4 = new List<string>();
            var ipv6 = new List<string>();
            foreach (UnicastIPAddressInformation addr in props.UnicastAddresses)
            {
                if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    ipv4.Add(addr.Address.ToString());
                }
                else if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                {
                    ipv6.Add(addr.Address.ToString());
                }
            }

            list.Add(new AdapterInfo
            {
                Id = nic.Id,
                Name = nic.Name,
                Description = nic.Description,
                Status = nic.OperationalStatus,
                Ipv4Index = ipv4Index,
                Ipv6Index = ipv6Index,
                UnicastIpv4 = ipv4,
                UnicastIpv6 = ipv6,
            });
        }

        return list;
    }

    private static IReadOnlyList<string> CaptureDns(string adapterId)
    {
        NetworkInterface? nic = NetworkInterface.GetAllNetworkInterfaces()
            .FirstOrDefault(n => string.Equals(n.Id, adapterId, StringComparison.OrdinalIgnoreCase));
        if (nic is null)
        {
            return [];
        }

        return nic.GetIPProperties().DnsAddresses
            .Select(a => a.ToString())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<DefaultRouteEntry> CaptureIPv4DefaultRoutes(IReadOnlyList<AdapterInfo> adapters)
        => ReadAllIPv4Routes(adapters)
            .Where(r => r.IsDefault)
            .Select(r => new DefaultRouteEntry
            {
                InterfaceIndex = r.InterfaceIndex,
                InterfaceName = r.InterfaceName,
                NextHop = r.NextHop.ToString(),
                RouteMetric = r.RouteMetric,
            })
            .ToList();

    private static IReadOnlyList<Ipv4RouteEntry> ReadAllIPv4Routes(IReadOnlyList<AdapterInfo> adapters)
    {
        var routes = new List<Ipv4RouteEntry>();
        foreach (IpForwardRow row in NativeIpForwardTable.ReadIPv4())
        {
            IPAddress dest = Ipv4FromNetworkOrderDword(row.ForwardDest);
            IPAddress mask = Ipv4FromNetworkOrderDword(row.ForwardMask);
            string name = adapters.FirstOrDefault(a => a.Ipv4Index == (int)row.ForwardIfIndex)?.Name
                ?? adapters.FirstOrDefault(a => a.Ipv6Index == (int)row.ForwardIfIndex)?.Name
                ?? $"ifIndex {row.ForwardIfIndex}";

            routes.Add(new Ipv4RouteEntry
            {
                Destination = dest,
                Mask = mask,
                NextHop = Ipv4FromNetworkOrderDword(row.ForwardNextHop),
                InterfaceIndex = (int)row.ForwardIfIndex,
                InterfaceName = name,
                RouteMetric = row.ForwardMetric1,
                ForwardProto = row.ForwardProto,
            });
        }

        return routes;
    }

    private static int? TryIndex(Func<int> read)
    {
        try
        {
            return read();
        }
        catch (NetworkInformationException)
        {
            return null;
        }
    }

    /// <summary>
    /// dwForwardNextHop is stored as an IPv4 address in network byte order
    /// (Microsoft Learn: MIB_IPFORWARDROW). On little-endian Windows the in-memory
    /// DWORD bytes are already the four address octets.
    /// </summary>
    private static IPAddress Ipv4FromNetworkOrderDword(uint value)
        => new(BitConverter.GetBytes(value));

    private static uint ToNetworkOrderDword(IPAddress address)
    {
        byte[] bytes = address.GetAddressBytes();
        if (bytes.Length != 4)
        {
            throw new ArgumentException("IPv4 address required.", nameof(address));
        }

        return BitConverter.ToUInt32(bytes, 0);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IpForwardRow
    {
        public uint ForwardDest;
        public uint ForwardMask;
        public uint ForwardPolicy;
        public uint ForwardNextHop;
        public uint ForwardIfIndex;
        public uint ForwardType;
        public uint ForwardProto;
        public uint ForwardAge;
        public uint ForwardNextHopAs;
        public uint ForwardMetric1;
        public uint ForwardMetric2;
        public uint ForwardMetric3;
        public uint ForwardMetric4;
        public uint ForwardMetric5;
    }

    private static class NativeIpForwardTable
    {
        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetIpForwardTable(IntPtr pIpForwardTable, ref uint pdwSize, bool bOrder);

        public static List<IpForwardRow> ReadIPv4()
        {
            uint size = 0;
            uint status = GetIpForwardTable(IntPtr.Zero, ref size, false);
            if (status == NoError && size == 0)
            {
                return [];
            }

            if (status != ErrorInsufficientBuffer && status != NoError)
            {
                throw new InvalidOperationException($"GetIpForwardTable size query failed: {status}");
            }

            IntPtr buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                status = GetIpForwardTable(buffer, ref size, true);
                if (status != NoError)
                {
                    throw new InvalidOperationException($"GetIpForwardTable failed: {status}");
                }

                int count = Marshal.ReadInt32(buffer);
                int offset = 4;
                int rowSize = Marshal.SizeOf<IpForwardRow>();
                var rows = new List<IpForwardRow>(count);
                for (int i = 0; i < count; i++)
                {
                    rows.Add(Marshal.PtrToStructure<IpForwardRow>(buffer + offset)!);
                    offset += rowSize;
                }

                return rows;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint DeleteIpForwardEntry(ref IpForwardRow pRoute);

        public static uint Delete(ref IpForwardRow row) => DeleteIpForwardEntry(ref row);
    }
}
