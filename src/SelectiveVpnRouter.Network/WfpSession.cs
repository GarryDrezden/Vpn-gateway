using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using SelectiveVpnRouter.Core;
using static SelectiveVpnRouter.Network.WfpNativeTypes;

namespace SelectiveVpnRouter.Network;

/// <summary>
/// User-mode WFP: dynamic session (fail-open on process death), IPv6 block filters,
/// optional callout filters when the KMDF driver is loaded.
/// Connect redirect classify lives in kernel; this class only adds/removes objects.
/// </summary>
public sealed class WfpSession : IWfpAppFilterInstaller, IDisposable
{
    public static readonly Guid ProviderKey = new("6b3d1f8a-7c2e-4b91-9e44-a1f0c3d5e607");
    public static readonly Guid SublayerKey = new("6b3d1f8a-7c2e-4b91-9e44-a1f0c3d5e608");
    public static readonly Guid CalloutV4Key = new("6b3d1f8a-7c2e-4b91-9e44-a1f0c3d5e609");
    public static readonly Guid LayerConnectRedirectV4 = new("c6e63c8c-b784-4562-aa7d-0a67cfcaf9a3");
    public static readonly Guid LayerAuthConnectV6 = WfpConstants.LayerAleAuthConnectV6;
    public static readonly Guid ConditionAleAppId = WfpConstants.ConditionAleAppId;

    private IntPtr _engine;
    private readonly List<Guid> _filters = [];
    private bool _driverPresent;

    static WfpSession()
    {
        if (OperatingSystem.IsWindows())
        {
            WfpAbiVerification.ThrowIfMismatch();
        }
    }

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

    public WfpPolicyApplyResult ReplaceVpnAppFilters(IReadOnlyList<string> exePaths, Ipv6Policy ipv6)
    {
        ClearFilters();
        var results = new List<WfpFilterInstallResult>();
        foreach (string exe in exePaths)
        {
            string fullPath = Path.GetFullPath(exe);
            if (_driverPresent)
            {
                results.Add(InstallAppCalloutFilter(fullPath));
            }

            if (ipv6 is Ipv6Policy.BlockForVpnRoutedApps)
            {
                results.Add(InstallIpv6BlockFilter(fullPath));
            }
        }

        int installed = results.Count(r => r.IsCalloutFilter && r.FilterInstalled);
        var apply = new WfpPolicyApplyResult
        {
            Filters = results,
            RequestedVpnApps = exePaths.Count,
            InstalledAppFilters = installed,
            DriverPresent = _driverPresent,
            SessionOpen = _engine != IntPtr.Zero,
            PolicyHealthy = exePaths.Count == 0 || installed >= exePaths.Count,
            LastError = BuildLastError(exePaths.Count, installed, results),
        };
        return apply;
    }

    private static string? BuildLastError(int requested, int installed, IReadOnlyList<WfpFilterInstallResult> results)
    {
        if (requested == 0 || installed >= requested)
        {
            return null;
        }

        WfpFilterInstallResult? failed = results.FirstOrDefault(r => r.IsCalloutFilter && !r.FilterInstalled);
        return failed is null
            ? $"Installed {installed}/{requested} callout app filters."
            : WfpPolicyHealth.FormatFilterLine(failed);
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

    private WfpFilterInstallResult InstallAppCalloutFilter(string fullPath)
        => InstallAppFilter(
            fullPath,
            LayerConnectRedirectV4,
            new FWPM_ACTION0
            {
                type = FWP_ACTION_TYPE.FWP_ACTION_CALLOUT_UNKNOWN,
                value = new FWPM_ACTION0_UNION { calloutKey = CalloutV4Key },
            },
            "SVR app redirect ",
            "Per-process TCP redirect",
            isCalloutFilter: true);

    private WfpFilterInstallResult InstallIpv6BlockFilter(string fullPath)
        => InstallAppFilter(
            fullPath,
            LayerAuthConnectV6,
            new FWPM_ACTION0
            {
                type = FWP_ACTION_TYPE.FWP_ACTION_BLOCK,
                value = new FWPM_ACTION0_UNION { filterType = Guid.Empty },
            },
            "SVR IPv6 block ",
            "Prevent IPv6 leak for VPN-routed app",
            isCalloutFilter: false);

    private WfpFilterInstallResult InstallAppFilter(
        string fullPath,
        Guid layer,
        FWPM_ACTION0 action,
        string namePrefix,
        string description,
        bool isCalloutFilter)
    {
        if (!File.Exists(fullPath))
        {
            return new WfpFilterInstallResult
            {
                ExePath = fullPath,
                FileExists = false,
                IsCalloutFilter = isCalloutFilter,
                Error = "File not found.",
            };
        }

        IntPtr appIdPtr = IntPtr.Zero;
        uint appIdStatus = Native.FwpmGetAppIdFromFileName0(fullPath, out appIdPtr);
        if (appIdStatus != 0 || appIdPtr == IntPtr.Zero)
        {
            return new WfpFilterInstallResult
            {
                ExePath = fullPath,
                FileExists = true,
                AppIdResolved = false,
                AppIdStatus = appIdStatus,
                IsCalloutFilter = isCalloutFilter,
                Error = "FwpmGetAppIdFromFileName0 failed.",
            };
        }

        try
        {
            var seed = new WfpFilterInstallResult
            {
                ExePath = fullPath,
                FileExists = true,
                AppIdResolved = true,
                AppIdStatus = appIdStatus,
                IsCalloutFilter = isCalloutFilter,
            };
            return AddFilter(fullPath, layer, action, namePrefix, description, appIdPtr, isCalloutFilter, seed);
        }
        finally
        {
            Native.FwpmFreeMemory0(ref appIdPtr);
        }
    }

    private WfpFilterInstallResult AddFilter(
        string fullPath,
        Guid layer,
        FWPM_ACTION0 action,
        string namePrefix,
        string description,
        IntPtr appIdBlobPtr,
        bool isCalloutFilter,
        WfpFilterInstallResult seed)
    {
        Guid filterKey = Guid.NewGuid();
        IntPtr condMem = IntPtr.Zero;
        IntPtr providerKeyPtr = IntPtr.Zero;
        IntPtr filterMem = IntPtr.Zero;
        try
        {
            var conditionValue = FWP_CONDITION_VALUE0.FromByteBlobPointer(appIdBlobPtr);
            WfpAppIdConditionValidation validation = WfpAppIdConditionDiagnostics.ValidateAppIdCondition(
                ConditionAleAppId,
                (uint)FWP_MATCH_TYPE.FWP_MATCH_EQUAL,
                (uint)conditionValue.type,
                appIdBlobPtr);
            if (!validation.IsValid)
            {
                return seed with
                {
                    FilterInstalled = false,
                    FilterAddStatus = 0,
                    Error = validation.Error + " " + validation.DiagnosticLine,
                };
            }

            var cond = new FWPM_FILTER_CONDITION0
            {
                fieldKey = ConditionAleAppId,
                matchType = FWP_MATCH_TYPE.FWP_MATCH_EQUAL,
                conditionValue = conditionValue,
            };
            condMem = Marshal.AllocHGlobal(Marshal.SizeOf<FWPM_FILTER_CONDITION0>());
            Marshal.StructureToPtr(cond, condMem, false);

            providerKeyPtr = Marshal.AllocHGlobal(Marshal.SizeOf<Guid>());
            Marshal.StructureToPtr(ProviderKey, providerKeyPtr, false);

            var filter = default(FWPM_FILTER0);
            filter.filterKey = filterKey;
            filter.displayData = new FWPM_DISPLAY_DATA0
            {
                name = namePrefix + Path.GetFileName(fullPath),
                description = description,
            };
            filter.providerKey = providerKeyPtr;
            filter.layerKey = layer;
            filter.subLayerKey = SublayerKey;
            filter.action = action;
            filter.numFilterConditions = 1;
            filter.filterCondition = condMem;
            filter.weight = FWP_VALUE0.Empty;

            filterMem = Marshal.AllocHGlobal(Marshal.SizeOf<FWPM_FILTER0>());
            Marshal.StructureToPtr(filter, filterMem, false);

            uint filterAddStatus = Native.FwpmFilterAdd0(_engine, filterMem, IntPtr.Zero, out ulong filterId);
            bool installed = filterAddStatus == 0 && filterId != 0;
            if (installed)
            {
                _filters.Add(filterKey);
            }

            string? error = installed
                ? null
                : "FwpmFilterAdd0 failed: " + WfpNativeStatus.Describe(filterAddStatus) + " " + validation.DiagnosticLine;

            return seed with
            {
                AppIdResolved = true,
                FilterInstalled = installed,
                FilterAddStatus = filterAddStatus,
                FilterId = filterId,
                IsCalloutFilter = isCalloutFilter,
                Error = error,
            };
        }
        finally
        {
            if (filterMem != IntPtr.Zero)
            {
                Marshal.DestroyStructure<FWPM_FILTER0>(filterMem);
                Marshal.FreeHGlobal(filterMem);
            }

            if (condMem != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(condMem);
            }

            if (providerKeyPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(providerKeyPtr);
            }
        }
    }
}

public static class WfpRedirectSockets
{
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

        uint addr = BitConverter.ToUInt32(context, 0);
        ushort portN = BitConverter.ToUInt16(context, 4);
        var ip = new IPAddress(BitConverter.GetBytes(addr));
        int port = (ushort)IPAddress.NetworkToHostOrder((short)portN);
        remote = new IPEndPoint(ip, port);
        return port > 0 && !ip.Equals(IPAddress.Any);
    }
}