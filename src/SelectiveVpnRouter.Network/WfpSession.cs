using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using SelectiveVpnRouter.Core;

namespace SelectiveVpnRouter.Network;

/// <summary>
/// User-mode WFP: dynamic session (fail-open on process death), IPv6 block filters,
/// optional callout filters when the KMDF driver is loaded.
/// Connect redirect classify lives in kernel; this class only adds/removes objects.
/// </summary>
public sealed class WfpSession : IDisposable
{
    public static readonly Guid ProviderKey = new("6b3d1f8a-7c2e-4b91-9e44-a1f0c3d5e607");
    public static readonly Guid SublayerKey = new("6b3d1f8a-7c2e-4b91-9e44-a1f0c3d5e608");
    public static readonly Guid CalloutV4Key = new("6b3d1f8a-7c2e-4b91-9e44-a1f0c3d5e609");
    public static readonly Guid LayerConnectRedirectV4 = new("c6e63c8c-b784-4562-aa7d-0a67cfcaf9a3");
    public static readonly Guid LayerAuthConnectV6 = new("4a72393b-319f-44bc-84c3-ba54dcb3b6b4");
    public static readonly Guid ConditionAleAppId = new("d78e1e87-8644-4ea5-9437-d809ecefc971");

    private IntPtr _engine;
    private readonly List<Guid> _filters = [];
    private bool _driverPresent;

    public bool DriverPresent => _driverPresent;

    public void Open(bool driverLoaded)
    {
        var session = new FWPM_SESSION0 { flags = 1 }; // FWPM_SESSION_FLAG_DYNAMIC
        uint st = Native.FwpmEngineOpen0(null, 10 /*RPC_C_AUTHN_WINNT*/, IntPtr.Zero, ref session, out _engine);
        if (st != 0)
        {
            throw new InvalidOperationException("FwpmEngineOpen0 0x" + st.ToString("X"));
        }

        AddProviderAndSublayer();
        _driverPresent = driverLoaded;
        if (driverLoaded)
        {
            TryAddCallout();
        }
    }

    public void ReplaceVpnAppFilters(IReadOnlyList<string> exePaths, Ipv6Policy ipv6)
    {
        ClearFilters();
        foreach (string exe in exePaths)
        {
            if (_driverPresent)
            {
                TryAddAppCalloutFilter(exe);
            }

            if (ipv6 is Ipv6Policy.BlockForVpnRoutedApps)
            {
                TryAddIpv6Block(exe);
            }
        }
    }

    public void ClearFilters()
    {
        foreach (Guid id in _filters)
        {
            Guid key = id;
            Native.FwpmFilterDeleteByKey0(_engine, ref key);
        }

        _filters.Clear();
    }

    public void Dispose()
    {
        try
        {
            ClearFilters();
        }
        catch (Exception)
        {
        }

        if (_engine != IntPtr.Zero)
        {
            Native.FwpmEngineClose0(_engine);
            _engine = IntPtr.Zero;
        }
    }

    private void AddProviderAndSublayer()
    {
        Guid pk = ProviderKey;
        var provider = new FWPM_PROVIDER0
        {
            providerKey = pk,
            displayData = new FWPM_DISPLAY_DATA0 { name = "Selective VPN Router", description = "Owned WFP provider" },
        };
        Native.FwpmProviderAdd0(_engine, ref provider, IntPtr.Zero);

        Guid sk = SublayerKey;
        var sub = new FWPM_SUBLAYER0
        {
            subLayerKey = sk,
            displayData = new FWPM_DISPLAY_DATA0 { name = "Selective VPN Router sublayer", description = "" },
            providerKey = IntPtr.Zero,
            weight = 0x8000,
        };
        Native.FwpmSubLayerAdd0(_engine, ref sub, IntPtr.Zero);
    }

    private bool TryAddCallout()
    {
        Guid ck = CalloutV4Key;
        Guid layer = LayerConnectRedirectV4;
        var callout = new FWPM_CALLOUT0
        {
            calloutKey = ck,
            displayData = new FWPM_DISPLAY_DATA0 { name = "SelectiveVpn connect redirect", description = "TCP connect redirect to local proxy" },
            applicableLayer = layer,
        };
        uint st = Native.FwpmCalloutAdd0(_engine, ref callout, IntPtr.Zero, out _);
        return st == 0 || st == 0x80320016; // already exists
    }

    private void TryAddAppCalloutFilter(string exe)
    {
        if (!TryAppId(exe, out FWP_BYTE_BLOB blob, out IntPtr alloc))
        {
            return;
        }

        try
        {
            Guid id = Guid.NewGuid();
            Guid layer = LayerConnectRedirectV4;
            Guid callout = CalloutV4Key;
            Guid sub = SublayerKey;
            var cond = new FWPM_FILTER_CONDITION0
            {
                fieldKey = ConditionAleAppId,
                matchType = 0, // FWP_MATCH_EQUAL
                conditionValue = new FWP_CONDITION_VALUE0 { type = 0x100 /*FWP_BYTE_BLOB_TYPE*/, value = alloc },
            };
            var filter = new FWPM_FILTER0
            {
                filterKey = id,
                displayData = new FWPM_DISPLAY_DATA0 { name = "SVR app redirect " + Path.GetFileName(exe), description = "Per-process TCP redirect" },
                layerKey = layer,
                subLayerKey = sub,
                action = new FWPM_ACTION0 { type = 0x00004005 /* FWP_ACTION_CALLOUT_UNKNOWN */ , filterType = callout },
                numFilterConditions = 1,
                filterCondition = IntPtr.Zero,
                weight = new FWP_VALUE0 { type = 0 },
            };
            IntPtr condMem = Marshal.AllocHGlobal(Marshal.SizeOf<FWPM_FILTER_CONDITION0>());
            Marshal.StructureToPtr(cond, condMem, false);
            filter.filterCondition = condMem;
            uint st = Native.FwpmFilterAdd0(_engine, ref filter, IntPtr.Zero, out _);
            Marshal.FreeHGlobal(condMem);
            if (st == 0)
            {
                _filters.Add(id);
            }
        }
        finally
        {
            if (alloc != IntPtr.Zero)
            {
                Native.FwpmFreeMemory0(ref alloc);
            }
        }

        _ = blob;
    }

    private void TryAddIpv6Block(string exe)
    {
        if (!TryAppId(exe, out _, out IntPtr alloc))
        {
            return;
        }

        try
        {
            Guid id = Guid.NewGuid();
            Guid layer = LayerAuthConnectV6;
            Guid sub = SublayerKey;
            var cond = new FWPM_FILTER_CONDITION0
            {
                fieldKey = ConditionAleAppId,
                matchType = 0,
                conditionValue = new FWP_CONDITION_VALUE0 { type = 0x100, value = alloc },
            };
            var filter = new FWPM_FILTER0
            {
                filterKey = id,
                displayData = new FWPM_DISPLAY_DATA0 { name = "SVR IPv6 block " + Path.GetFileName(exe), description = "Prevent IPv6 leak for VPN-routed app" },
                layerKey = layer,
                subLayerKey = sub,
                action = new FWPM_ACTION0 { type = 0x00001001 /* FWP_ACTION_BLOCK */ },
                numFilterConditions = 1,
                weight = new FWP_VALUE0 { type = 0 },
            };
            IntPtr condMem = Marshal.AllocHGlobal(Marshal.SizeOf<FWPM_FILTER_CONDITION0>());
            Marshal.StructureToPtr(cond, condMem, false);
            filter.filterCondition = condMem;
            uint st = Native.FwpmFilterAdd0(_engine, ref filter, IntPtr.Zero, out _);
            Marshal.FreeHGlobal(condMem);
            if (st == 0)
            {
                _filters.Add(id);
            }
        }
        finally
        {
            Native.FwpmFreeMemory0(ref alloc);
        }
    }

    private bool TryAppId(string exe, out FWP_BYTE_BLOB blob, out IntPtr alloc)
    {
        blob = default;
        alloc = IntPtr.Zero;
        uint st = Native.FwpmGetAppIdFromFileName0(exe, out alloc);
        return st == 0 && alloc != IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FWPM_DISPLAY_DATA0
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string name;
        [MarshalAs(UnmanagedType.LPWStr)] public string description;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FWPM_SESSION0
    {
        public Guid sessionKey;
        public FWPM_DISPLAY_DATA0 displayData;
        public uint flags;
        public uint txnWaitTimeoutInMSec;
        public int processId;
        public IntPtr sid;
        [MarshalAs(UnmanagedType.LPWStr)] public string? username;
        [MarshalAs(UnmanagedType.Bool)] public bool kernelMode;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FWPM_PROVIDER0
    {
        public Guid providerKey;
        public FWPM_DISPLAY_DATA0 displayData;
        public uint flags;
        public FWP_BYTE_BLOB providerData;
        [MarshalAs(UnmanagedType.LPWStr)] public string? serviceName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FWPM_SUBLAYER0
    {
        public Guid subLayerKey;
        public FWPM_DISPLAY_DATA0 displayData;
        public uint flags;
        public IntPtr providerKey;
        public FWP_BYTE_BLOB providerData;
        public ushort weight;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FWPM_CALLOUT0
    {
        public Guid calloutKey;
        public FWPM_DISPLAY_DATA0 displayData;
        public uint flags;
        public IntPtr providerKey;
        public FWP_BYTE_BLOB providerData;
        public Guid applicableLayer;
        public uint calloutId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FWP_BYTE_BLOB
    {
        public uint size;
        public IntPtr data;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FWP_VALUE0
    {
        public uint type;
        public uint pad;
        public IntPtr value0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FWP_CONDITION_VALUE0
    {
        public uint type;
        public uint pad;
        public IntPtr value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FWPM_FILTER_CONDITION0
    {
        public Guid fieldKey;
        public uint matchType;
        public FWP_CONDITION_VALUE0 conditionValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FWPM_ACTION0
    {
        public uint type;
        public Guid filterType;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FWPM_FILTER0
    {
        public Guid filterKey;
        public FWPM_DISPLAY_DATA0 displayData;
        public uint flags;
        public IntPtr providerKey;
        public FWP_BYTE_BLOB providerData;
        public Guid layerKey;
        public Guid subLayerKey;
        public FWP_VALUE0 weight;
        public uint numFilterConditions;
        public IntPtr filterCondition;
        public FWPM_ACTION0 action;
        public ulong rawContext;
        public Guid reserved;
        public ulong filterId;
        public FWP_VALUE0 effectiveWeight;
    }

    private static class Native
    {
        [DllImport("fwpuclnt.dll", CharSet = CharSet.Unicode)]
        public static extern uint FwpmEngineOpen0(string? serverName, uint authnService, IntPtr authIdentity, ref FWPM_SESSION0 session, out IntPtr engine);

        [DllImport("fwpuclnt.dll")]
        public static extern uint FwpmEngineClose0(IntPtr engine);

        [DllImport("fwpuclnt.dll")]
        public static extern uint FwpmProviderAdd0(IntPtr engine, ref FWPM_PROVIDER0 provider, IntPtr sd);

        [DllImport("fwpuclnt.dll")]
        public static extern uint FwpmSubLayerAdd0(IntPtr engine, ref FWPM_SUBLAYER0 sub, IntPtr sd);

        [DllImport("fwpuclnt.dll")]
        public static extern uint FwpmCalloutAdd0(IntPtr engine, ref FWPM_CALLOUT0 callout, IntPtr sd, out uint id);

        [DllImport("fwpuclnt.dll")]
        public static extern uint FwpmFilterAdd0(IntPtr engine, ref FWPM_FILTER0 filter, IntPtr sd, out ulong id);

        [DllImport("fwpuclnt.dll")]
        public static extern uint FwpmFilterDeleteByKey0(IntPtr engine, ref Guid key);

        [DllImport("fwpuclnt.dll", CharSet = CharSet.Unicode)]
        public static extern uint FwpmGetAppIdFromFileName0(string fileName, out IntPtr appId);

        [DllImport("fwpuclnt.dll")]
        public static extern void FwpmFreeMemory0(ref IntPtr p);
    }
}

public static class WfpRedirectSockets
{
    private const int SioQueryRecords = unchecked((int)0x980000DC); // _WSAIOW(IOC_VENDOR, 220) computed below
    private const int SioQueryContext = unchecked((int)0x980000DD);
    private const int SioSetRecords = unchecked((int)0x980000DE);

    // IOC_VENDOR=0x18000000, _WSAIOW = IOC_IN|IOC_VENDOR|code. IOC_IN=0x80000000
    // SIO_QUERY_WFP_CONNECTION_REDIRECT_RECORDS = 0x980000DC (220)
    // We'll use IOControlCode from mstcpip: 0x80000000 | 0x18000000 | 220 = 0x980000DC

    public static byte[] QueryRedirectRecords(Socket accepted)
    {
        byte[] buf = new byte[2048];
        int got = accepted.IOControl(unchecked((IOControlCode)0x980000DC), null, buf);
        if (got <= 0)
        {
            return [];
        }

        var exact = new byte[got];
        Buffer.BlockCopy(buf, 0, exact, 0, got);
        return exact;
    }

    public static byte[] QueryRedirectContext(Socket accepted)
    {
        byte[] buf = new byte[512];
        try
        {
            int got = accepted.IOControl(unchecked((IOControlCode)0x980000DD), null, buf);
            if (got <= 0)
            {
                return [];
            }

            var exact = new byte[got];
            Buffer.BlockCopy(buf, 0, exact, 0, got);
            return exact;
        }
        catch (SocketException)
        {
            return [];
        }
    }

    public static void SetRedirectRecords(Socket outbound, byte[] records)
    {
        if (records.Length == 0)
        {
            return;
        }

        outbound.IOControl(unchecked((IOControlCode)0x980000DE), records, null);
    }

    public static bool TryParseContext(byte[] context, out IPEndPoint remote)
    {
        remote = new IPEndPoint(IPAddress.Any, 0);
        if (context.Length < 8)
        {
            return false;
        }

        // Our driver writes: uint ipv4 (network), ushort port (network), pad
        uint addr = BitConverter.ToUInt32(context, 0);
        ushort portN = BitConverter.ToUInt16(context, 4);
        var ip = new IPAddress(BitConverter.GetBytes(addr));
        int port = (ushort)IPAddress.NetworkToHostOrder((short)portN);
        remote = new IPEndPoint(ip, port);
        return port > 0 && !ip.Equals(IPAddress.Any);
    }
}
