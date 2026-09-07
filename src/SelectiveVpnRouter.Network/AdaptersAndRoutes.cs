using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using SelectiveVpnRouter.Core;

namespace SelectiveVpnRouter.Network;

public sealed record AdapterView(
    string Id,
    string Name,
    string Description,
    OperationalStatus Status,
    int? Ipv4Index,
    int? Ipv6Index,
    IReadOnlyList<string> Ipv4,
    IReadOnlyList<string> Ipv6);

public sealed record RouteRow(
    IPAddress Destination,
    IPAddress Mask,
    IPAddress NextHop,
    int InterfaceIndex,
    uint Metric,
    uint Proto);

public static class AdapterCatalog
{
    public static IReadOnlyList<AdapterView> All()
    {
        var list = new List<AdapterView>();
        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            IPInterfaceProperties props = nic.GetIPProperties();
            int? v4 = Try(() => props.GetIPv4Properties().Index);
            int? v6 = Try(() => props.GetIPv6Properties().Index);
            list.Add(new AdapterView(
                nic.Id,
                nic.Name,
                nic.Description,
                nic.OperationalStatus,
                v4,
                v6,
                props.UnicastAddresses.Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    .Select(a => a.Address.ToString()).ToList(),
                props.UnicastAddresses.Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                    .Select(a => a.Address.ToString()).ToList()));
        }

        return list;
    }

    public static AdapterView? GuessVpn(IReadOnlyList<AdapterView> before, IReadOnlyList<AdapterView> after)
    {
        foreach (AdapterView nic in after.Where(a => a.Status == OperationalStatus.Up))
        {
            AdapterView? prev = before.FirstOrDefault(b => string.Equals(b.Id, nic.Id, StringComparison.OrdinalIgnoreCase));
            bool looks = LooksVpn(nic.Name) || LooksVpn(nic.Description);
            bool newborn = prev is null;
            bool cameUp = prev is not null && prev.Status != OperationalStatus.Up && nic.Ipv4.Count > 0;
            bool ipChanged = prev is not null && !prev.Ipv4.SequenceEqual(nic.Ipv4) && nic.Ipv4.Count > 0;
            if (newborn || cameUp || (looks && ipChanged) || (looks && prev is null))
            {
                return nic;
            }
        }

        return after.FirstOrDefault(a => a.Status == OperationalStatus.Up && (LooksVpn(a.Name) || LooksVpn(a.Description)));
    }

    public static bool LooksVpn(string text)
    {
        string t = text.ToLowerInvariant();
        return t.Contains("openvpn") || t.Contains("wintun") || t.Contains("ovpn") || t.Contains("tap-windows") || t.Contains("tap-win");
    }

    private static int? Try(Func<int> f)
    {
        try { return f(); }
        catch (NetworkInformationException) { return null; }
    }
}

public static class RouteTable
{
    private const uint ErrorInsufficientBuffer = 122;
    private const uint NoError = 0;
    private const uint MibIpProtoNetMgmt = 3;

    public static IReadOnlyList<RouteRow> IPv4()
    {
        var adapters = AdapterCatalog.All();
        var rows = new List<RouteRow>();
        foreach (IpForwardRow row in Native.Read())
        {
            rows.Add(new RouteRow(
                ToIp(row.ForwardDest),
                ToIp(row.ForwardMask),
                ToIp(row.ForwardNextHop),
                (int)row.ForwardIfIndex,
                row.ForwardMetric1,
                row.ForwardProto));
        }

        _ = adapters;
        return rows;
    }

    public static IReadOnlyList<RouteRow> DefaultRoutes()
        => IPv4().Where(r => r.Destination.Equals(IPAddress.Any) && r.Mask.Equals(IPAddress.Any)).ToList();

    public static bool TryAddOwned(OwnedRoute route, out string error)
    {
        error = "";
        if (!TryParsePrefix(route.DestinationPrefix, out IPAddress dest, out IPAddress mask))
        {
            error = "Invalid prefix " + route.DestinationPrefix;
            return false;
        }

        if (!IPAddress.TryParse(route.NextHop, out IPAddress? hop))
        {
            error = "Invalid next hop";
            return false;
        }

        var row = new IpForwardRow
        {
            ForwardDest = ToDword(dest),
            ForwardMask = ToDword(mask),
            ForwardNextHop = ToDword(hop),
            ForwardIfIndex = (uint)route.InterfaceIndex,
            ForwardType = 4,
            ForwardProto = MibIpProtoNetMgmt,
            ForwardMetric1 = route.Metric,
            ForwardMetric2 = uint.MaxValue,
            ForwardMetric3 = uint.MaxValue,
            ForwardMetric4 = uint.MaxValue,
            ForwardMetric5 = uint.MaxValue,
        };
        uint st = Native.Create(ref row);
        if (st != NoError)
        {
            error = "CreateIpForwardEntry " + st;
            return false;
        }

        return true;
    }

    public static bool TryDeleteOwned(OwnedRoute route, out string error)
    {
        error = "";
        if (!TryParsePrefix(route.DestinationPrefix, out IPAddress dest, out IPAddress mask)
            || !IPAddress.TryParse(route.NextHop, out IPAddress? hop))
        {
            error = "Invalid owned route identity";
            return false;
        }

        IpForwardRow[] matches = Native.Read()
            .Where(r => r.ForwardDest == ToDword(dest)
                && r.ForwardMask == ToDword(mask)
                && r.ForwardIfIndex == (uint)route.InterfaceIndex
                && r.ForwardNextHop == ToDword(hop))
            .ToArray();
        if (matches.Length != 1)
        {
            error = matches.Length == 0 ? "Route already gone." : "Not unique; refusing delete.";
            return matches.Length == 0;
        }

        IpForwardRow row = matches[0];
        row.ForwardProto = MibIpProtoNetMgmt;
        uint st = Native.Delete(ref row);
        if (st != NoError)
        {
            error = "DeleteIpForwardEntry " + st;
            return false;
        }

        return true;
    }

    public static bool HasInternetWideVia(int ifIndex)
        => IPv4().Any(r => r.InterfaceIndex == ifIndex && IsInternetWide(r) && r.Metric < 5000);

    public static bool IsInternetWide(RouteRow r)
    {
        var slash1 = IPAddress.Parse("128.0.0.0");
        return (r.Destination.Equals(IPAddress.Any) && r.Mask.Equals(IPAddress.Any))
            || (r.Mask.Equals(slash1) && (r.Destination.Equals(IPAddress.Any) || r.Destination.Equals(slash1)));
    }

    private static bool TryParsePrefix(string prefix, out IPAddress dest, out IPAddress mask)
    {
        dest = IPAddress.Any;
        mask = IPAddress.Any;
        string[] parts = prefix.Split('/');
        if (!IPAddress.TryParse(parts[0], out dest!))
        {
            return false;
        }

        int bits = parts.Length > 1 && int.TryParse(parts[1], out int b) ? b : 32;
        uint m = bits == 0 ? 0 : uint.MaxValue << (32 - bits);
        mask = new IPAddress(BitConverter.GetBytes(IPAddress.HostToNetworkOrder((int)m)));
        if (bits == 32)
        {
            mask = IPAddress.Broadcast;
        }

        if (bits == 0)
        {
            mask = IPAddress.Any;
        }

        return true;
    }

    private static IPAddress ToIp(uint v) => new(BitConverter.GetBytes(v));
    private static uint ToDword(IPAddress a) => BitConverter.ToUInt32(a.GetAddressBytes(), 0);

    [StructLayout(LayoutKind.Sequential)]
    private struct IpForwardRow
    {
        public uint ForwardDest, ForwardMask, ForwardPolicy, ForwardNextHop, ForwardIfIndex;
        public uint ForwardType, ForwardProto, ForwardAge, ForwardNextHopAs;
        public uint ForwardMetric1, ForwardMetric2, ForwardMetric3, ForwardMetric4, ForwardMetric5;
    }

    private static class Native
    {
        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetIpForwardTable(IntPtr table, ref uint size, bool order);

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint CreateIpForwardEntry(ref IpForwardRow row);

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint DeleteIpForwardEntry(ref IpForwardRow row);

        public static uint Create(ref IpForwardRow row) => CreateIpForwardEntry(ref row);
        public static uint Delete(ref IpForwardRow row) => DeleteIpForwardEntry(ref row);

        public static List<IpForwardRow> Read()
        {
            uint size = 0;
            uint st = GetIpForwardTable(IntPtr.Zero, ref size, false);
            if (st != ErrorInsufficientBuffer && st != NoError)
            {
                throw new InvalidOperationException("GetIpForwardTable " + st);
            }

            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try
            {
                st = GetIpForwardTable(buf, ref size, true);
                if (st != NoError)
                {
                    throw new InvalidOperationException("GetIpForwardTable " + st);
                }

                int count = Marshal.ReadInt32(buf);
                int offset = 4;
                int rowSize = Marshal.SizeOf<IpForwardRow>();
                var rows = new List<IpForwardRow>(count);
                for (int i = 0; i < count; i++)
                {
                    rows.Add(Marshal.PtrToStructure<IpForwardRow>(buf + offset)!);
                    offset += rowSize;
                }

                return rows;
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }
    }
}
