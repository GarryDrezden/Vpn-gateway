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
public sealed class WfpSession : IWfpAppFilterInstaller, IWfpRuntimeCaptureFilterInstaller, IDisposable
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

    public WfpPolicyApplyResult ReplaceVpnAppFilters(
        IReadOnlyList<string> exePaths,
        Ipv6Policy ipv6,
        WfpAppFilterOptions? options = null)
    {
        ClearFilters();
        options ??= new WfpAppFilterOptions();
        var results = new List<WfpFilterInstallResult>();

        foreach (string exe in exePaths)
        {
            string displayPath = WfpAppIdentity.GetDisplayPath(exe);
            bool useIdentityOverride = ShouldApplyIdentityOverride(exe, options, exePaths.Count);
            string identityInput = useIdentityOverride ? options.WfpIdentitySourceOverride! : exe;
            WfpAppIdentityPathMode identityMode = useIdentityOverride
                ? options.IdentityPathMode
                : WfpAppIdentityPathMode.Default;
            foreach (string identityPath in WfpAppIdentity.GetIdentityPaths(identityInput, identityMode))
            {
                bool shortFallback = IsShortPathFallback(displayPath, identityPath);
                if (_driverPresent)
                {
                    results.Add(InstallAppLoopbackPermitFilter(identityPath, displayPath, shortFallback));
                    results.Add(InstallAppCalloutFilter(identityPath, displayPath, shortFallback));
                }

                if (ipv6 is Ipv6Policy.BlockForVpnRoutedApps)
                {
                    results.Add(InstallIpv6BlockFilter(identityPath, displayPath, shortFallback));
                }
            }
        }

        int installedCallout = results.Count(r => r.Role == WfpFilterRole.RedirectCallout && r.FilterInstalled);
        bool eachAppReady = exePaths.All(exe =>
        {
            string display = WfpAppIdentity.GetDisplayPath(exe);
            bool redirect = results.Any(r =>
                r.Role == WfpFilterRole.RedirectCallout
                && r.FilterInstalled
                && string.Equals(r.ExePath, display, StringComparison.OrdinalIgnoreCase));
            bool loopback = results.Any(r =>
                r.Role == WfpFilterRole.LoopbackPermitV4
                && r.FilterInstalled
                && string.Equals(r.ExePath, display, StringComparison.OrdinalIgnoreCase));
            return redirect && loopback;
        });

        var apply = new WfpPolicyApplyResult
        {
            Filters = results,
            RequestedVpnApps = exePaths.Count,
            InstalledAppFilters = installedCallout,
            DriverPresent = _driverPresent,
            SessionOpen = _engine != IntPtr.Zero,
            PolicyHealthy = exePaths.Count == 0 || eachAppReady,
            LastError = BuildLastError(exePaths, eachAppReady, results),
        };
        return apply;
    }

    private static bool ShouldApplyIdentityOverride(string exe, WfpAppFilterOptions options, int exePathCount)
    {
        if (string.IsNullOrWhiteSpace(options.WfpIdentitySourceOverride))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(options.WfpIdentityOverrideExePath))
        {
            return string.Equals(
                WfpAppIdentity.GetDisplayPath(exe),
                WfpAppIdentity.GetDisplayPath(options.WfpIdentityOverrideExePath),
                StringComparison.OrdinalIgnoreCase);
        }

        return exePathCount == 1;
    }

    private static bool IsShortPathFallback(string displayPath, string identityPath)
    {
        if (WfpAppIdentity.PathsEquivalent(displayPath, identityPath))
        {
            return false;
        }

        return WfpAppIdentity.ContainsNonAscii(displayPath)
            && !WfpAppIdentity.ContainsNonAscii(identityPath);
    }

    private static string? BuildLastError(
        IReadOnlyList<string> requestedPaths,
        bool eachAppReady,
        IReadOnlyList<WfpFilterInstallResult> results)
    {
        if (requestedPaths.Count == 0 || eachAppReady)
        {
            return null;
        }

        WfpFilterInstallResult? failed = results.FirstOrDefault(r =>
            (r.Role == WfpFilterRole.RedirectCallout || r.Role == WfpFilterRole.LoopbackPermitV4)
            && !r.FilterInstalled);
        return failed is null
            ? "At least one VPN app is missing redirect and/or loopback permit WFP filters."
            : WfpPolicyHealth.FormatFilterLine(failed);
    }

    public bool SessionOpen => _engine != IntPtr.Zero;

    public void ClearFilters()
    {
        foreach (Guid id in _filters)
        {
            Guid key = id;
            Native.FwpmFilterDeleteByKey0(_engine, ref key);
        }

        _filters.Clear();
    }

    public bool TryAddRuntimeAppIdCaptureFilter(out WfpRuntimeCaptureFilterResult result)
        => TryAddRuntimeAppIdCaptureFilter(3, WfpRuntimeAppIdCapture.DiagnosticRemoteAddressUInt32, out result);

    public bool TryAddRuntimeAppIdCaptureFilterWithAppId(
        IntPtr fwpmAppIdBlobPtr,
        out WfpRuntimeCaptureFilterResult result)
        => TryAddRuntimeAppIdCaptureFilterWithAppId(
            fwpmAppIdBlobPtr,
            WfpRuntimeAppIdCapture.DiagnosticRemoteAddressUInt32,
            out result);

    public bool TryAddRuntimeAppIdCaptureFilterWithAppId(
        IntPtr fwpmAppIdBlobPtr,
        uint remoteAddressUInt32,
        out WfpRuntimeCaptureFilterResult result)
    {
        if (_engine == IntPtr.Zero)
        {
            result = new WfpRuntimeCaptureFilterResult(
                0,
                0,
                Guid.Empty,
                "Production WFP session is not open.");
            return false;
        }

        if (fwpmAppIdBlobPtr == IntPtr.Zero)
        {
            result = new WfpRuntimeCaptureFilterResult(0, 0, Guid.Empty, "APP_ID blob is null.");
            return false;
        }

        var appIdConditionValue = FWP_CONDITION_VALUE0.FromByteBlobPointer(fwpmAppIdBlobPtr);
        WfpAppIdConditionValidation validation = WfpAppIdConditionDiagnostics.ValidateAppIdCondition(
            ConditionAleAppId,
            (uint)FWP_MATCH_TYPE.FWP_MATCH_EQUAL,
            (uint)appIdConditionValue.type,
            fwpmAppIdBlobPtr);
        if (!validation.IsValid)
        {
            result = new WfpRuntimeCaptureFilterResult(
                0,
                0,
                Guid.Empty,
                validation.Error + " " + validation.DiagnosticLine);
            return false;
        }

        FWPM_FILTER_CONDITION0[] conditions =
        [
            BuildRuntimeCaptureUint8Condition(WfpConstants.ConditionIpProtocol, 6),
            BuildRuntimeCaptureUint16Condition(WfpConstants.ConditionIpRemotePort, WfpRuntimeAppIdCapture.DiagnosticRemotePort),
            BuildRuntimeCaptureUint32Condition(WfpConstants.ConditionIpRemoteAddress, remoteAddressUInt32),
            new FWPM_FILTER_CONDITION0
            {
                fieldKey = ConditionAleAppId,
                matchType = FWP_MATCH_TYPE.FWP_MATCH_EQUAL,
                conditionValue = appIdConditionValue,
            },
        ];

        return TryAddRuntimeCaptureFilterWithConditions(conditions, out result);
    }

    public bool TryAddRuntimeAppIdCaptureFilter(
        int conditionCount,
        uint remoteAddressUInt32,
        out WfpRuntimeCaptureFilterResult result)
    {
        if (_engine == IntPtr.Zero)
        {
            result = new WfpRuntimeCaptureFilterResult(
                0,
                0,
                Guid.Empty,
                "Production WFP session is not open.");
            return false;
        }

        if (conditionCount is < 1 or > 3)
        {
            result = new WfpRuntimeCaptureFilterResult(
                0,
                0,
                Guid.Empty,
                "conditionCount must be 1..3.");
            return false;
        }

        return TryAddRuntimeCaptureFilterWithConditions(
            BuildRuntimeCaptureConditions(conditionCount, remoteAddressUInt32),
            out result);
    }

    public static WfpRuntimeCaptureFilterSetup RuntimeCaptureFilterSetupTemplate { get; } = new(
        LayerConnectRedirectV4,
        SublayerKey,
        CalloutV4Key,
        WfpRuntimeAppIdCapture.FilterRawContext,
        (uint)FWP_ACTION_TYPE.FWP_ACTION_CALLOUT_UNKNOWN,
        "FWP_VALUE0.Empty (default weight)");

    public void RemoveRuntimeAppIdCaptureFilter(Guid filterKey)
    {
        if (_engine == IntPtr.Zero || filterKey == Guid.Empty)
        {
            return;
        }

        Guid key = filterKey;
        Native.FwpmFilterDeleteByKey0(_engine, ref key);
    }

    public IReadOnlyList<WfpRuntimeCaptureStepProbeResult> ProbeRuntimeCaptureFilterSteps()
    {
        if (_engine == IntPtr.Zero)
        {
            return [];
        }

        (string set, int count)[] steps =
        [
            ("A:IP_PROTOCOL", 1),
            ("B:IP_PROTOCOL+IP_REMOTE_PORT", 2),
            ("C:IP_PROTOCOL+IP_REMOTE_PORT+IP_REMOTE_ADDRESS", 3),
        ];

        var results = new List<WfpRuntimeCaptureStepProbeResult>(steps.Length);
        foreach ((string set, int count) in steps)
        {
            FWPM_FILTER_CONDITION0[] conditions = BuildRuntimeCaptureConditions(count, WfpRuntimeAppIdCapture.DiagnosticRemoteAddressUInt32);
            IReadOnlyList<string> conditionLog = WfpRuntimeCaptureConditionLayout.DescribeConditions(conditions);
            if (!TryAddRuntimeCaptureFilterWithConditions(conditions, out WfpRuntimeCaptureFilterResult addResult))
            {
                results.Add(new WfpRuntimeCaptureStepProbeResult(
                    set,
                    addResult.FilterAddStatus,
                    addResult.FilterId,
                    conditionLog));
                continue;
            }

            RemoveRuntimeAppIdCaptureFilter(addResult.FilterKey);
            results.Add(new WfpRuntimeCaptureStepProbeResult(
                set,
                addResult.FilterAddStatus,
                addResult.FilterId,
                conditionLog));
        }

        return results;
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
            displayData = new FWPM_DISPLAY_DATA0 { name = "VPN Route", description = "Owned WFP provider" },
        };
        Native.FwpmProviderAdd0(_engine, ref provider, IntPtr.Zero);

        Guid sk = SublayerKey;
        var sub = new FWPM_SUBLAYER0
        {
            subLayerKey = sk,
            displayData = new FWPM_DISPLAY_DATA0 { name = "VPN Route sublayer", description = "" },
            providerKey = IntPtr.Zero,
            weight = 0x8000,
        };
        Native.FwpmSubLayerAdd0(_engine, ref sub, IntPtr.Zero);
    }

    private static FWPM_FILTER_CONDITION0[] BuildRuntimeCaptureConditions(int count, uint remoteAddressUInt32)
    {
        var all = new[]
        {
            BuildRuntimeCaptureUint8Condition(WfpConstants.ConditionIpProtocol, 6),
            BuildRuntimeCaptureUint16Condition(WfpConstants.ConditionIpRemotePort, WfpRuntimeAppIdCapture.DiagnosticRemotePort),
            BuildRuntimeCaptureUint32Condition(WfpConstants.ConditionIpRemoteAddress, remoteAddressUInt32),
        };

        if (count < 1 || count > all.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        var slice = new FWPM_FILTER_CONDITION0[count];
        Array.Copy(all, slice, count);
        return slice;
    }

    private bool TryAddRuntimeCaptureFilterWithConditions(
        FWPM_FILTER_CONDITION0[] conditions,
        out WfpRuntimeCaptureFilterResult result)
    {
        Guid filterKey = Guid.NewGuid();
        int condSize = Marshal.SizeOf<FWPM_FILTER_CONDITION0>();
        IntPtr condBlock = Marshal.AllocHGlobal(condSize * conditions.Length);
        IntPtr providerKeyPtr = IntPtr.Zero;
        IntPtr filterMem = IntPtr.Zero;
        try
        {
            for (int i = 0; i < conditions.Length; i++)
            {
                Marshal.StructureToPtr(conditions[i], IntPtr.Add(condBlock, i * condSize), false);
            }

            providerKeyPtr = Marshal.AllocHGlobal(Marshal.SizeOf<Guid>());
            Marshal.StructureToPtr(ProviderKey, providerKeyPtr, false);

            var filter = default(FWPM_FILTER0);
            filter.filterKey = filterKey;
            filter.displayData = new FWPM_DISPLAY_DATA0
            {
                name = "SVR runtime APP_ID capture",
                description = "Diagnostic TCP "
                    + WfpRuntimeAppIdCapture.DiagnosticRemoteHost
                    + ":"
                    + WfpRuntimeAppIdCapture.DiagnosticRemotePort,
            };
            filter.providerKey = providerKeyPtr;
            filter.layerKey = LayerConnectRedirectV4;
            filter.subLayerKey = SublayerKey;
            filter.action = new FWPM_ACTION0
            {
                type = FWP_ACTION_TYPE.FWP_ACTION_CALLOUT_UNKNOWN,
                value = new FWPM_ACTION0_UNION { calloutKey = CalloutV4Key },
            };
            filter.numFilterConditions = (uint)conditions.Length;
            filter.filterCondition = condBlock;
            filter.weight = FWP_VALUE0.Empty;
            filter.context = new FWPM_FILTER_CONTEXT0 { rawContext = WfpRuntimeAppIdCapture.FilterRawContext };

            filterMem = Marshal.AllocHGlobal(Marshal.SizeOf<FWPM_FILTER0>());
            Marshal.StructureToPtr(filter, filterMem, false);

            uint filterAddStatus = Native.FwpmFilterAdd0(_engine, filterMem, IntPtr.Zero, out ulong filterId);
            if (filterAddStatus != 0 || filterId == 0)
            {
                result = new WfpRuntimeCaptureFilterResult(
                    filterAddStatus,
                    filterId,
                    Guid.Empty,
                    "FwpmFilterAdd0 failed: " + WfpNativeStatus.Describe(filterAddStatus));
                return false;
            }

            result = new WfpRuntimeCaptureFilterResult(filterAddStatus, filterId, filterKey, null);
            return true;
        }
        finally
        {
            if (filterMem != IntPtr.Zero)
            {
                Marshal.DestroyStructure<FWPM_FILTER0>(filterMem);
                Marshal.FreeHGlobal(filterMem);
            }

            for (int i = 0; i < conditions.Length; i++)
            {
                IntPtr slot = IntPtr.Add(condBlock, i * condSize);
                Marshal.DestroyStructure<FWPM_FILTER_CONDITION0>(slot);
            }

            Marshal.FreeHGlobal(condBlock);
            if (providerKeyPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(providerKeyPtr);
            }
        }
    }

    private static FWPM_FILTER_CONDITION0 BuildRuntimeCaptureUint8Condition(Guid fieldKey, byte value) =>
        new()
        {
            fieldKey = fieldKey,
            matchType = FWP_MATCH_TYPE.FWP_MATCH_EQUAL,
            conditionValue = new FWP_CONDITION_VALUE0
            {
                type = FWP_DATA_TYPE.FWP_UINT8,
                value = new FWP_VALUE0_UNION { uint8 = value },
            },
        };

    private static FWPM_FILTER_CONDITION0 BuildRuntimeCaptureUint16Condition(Guid fieldKey, ushort value) =>
        new()
        {
            fieldKey = fieldKey,
            matchType = FWP_MATCH_TYPE.FWP_MATCH_EQUAL,
            conditionValue = new FWP_CONDITION_VALUE0
            {
                type = FWP_DATA_TYPE.FWP_UINT16,
                value = new FWP_VALUE0_UNION { uint16 = value },
            },
        };

    private static FWPM_FILTER_CONDITION0 BuildRuntimeCaptureUint32Condition(Guid fieldKey, uint value) =>
        new()
        {
            fieldKey = fieldKey,
            matchType = FWP_MATCH_TYPE.FWP_MATCH_EQUAL,
            conditionValue = new FWP_CONDITION_VALUE0
            {
                type = FWP_DATA_TYPE.FWP_UINT32,
                value = new FWP_VALUE0_UNION { uint32 = value },
            },
        };

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

    private WfpFilterInstallResult InstallAppCalloutFilter(string identityPath, string displayExePath, bool shortFallback)
        => InstallAppFilter(
            identityPath,
            displayExePath,
            shortFallback,
            LayerConnectRedirectV4,
            new FWPM_ACTION0
            {
                type = FWP_ACTION_TYPE.FWP_ACTION_CALLOUT_UNKNOWN,
                value = new FWPM_ACTION0_UNION { calloutKey = CalloutV4Key },
            },
            "SVR app redirect ",
            "Per-process TCP redirect",
            isCalloutFilter: true,
            role: WfpFilterRole.RedirectCallout,
            weight: WfpVpnAppFilterPlanner.RedirectCalloutFilterWeight,
            extraConditions: null);

    private WfpFilterInstallResult InstallAppLoopbackPermitFilter(string identityPath, string displayExePath, bool shortFallback)
    {
        IntPtr v4MaskPtr = IntPtr.Zero;
        try
        {
            v4MaskPtr = Marshal.AllocHGlobal(Marshal.SizeOf<FWP_V4_ADDR_AND_MASK0>());
            var v4 = new FWP_V4_ADDR_AND_MASK0
            {
                addr = WfpLoopbackIpv4.PermitNetworkAddress,
                mask = WfpLoopbackIpv4.PermitNetworkMask,
            };
            Marshal.StructureToPtr(v4, v4MaskPtr, false);
            FWPM_FILTER_CONDITION0[] extra =
            [
                BuildRuntimeCaptureUint8Condition(WfpConstants.ConditionIpProtocol, 6),
                new FWPM_FILTER_CONDITION0
                {
                    fieldKey = WfpConstants.ConditionIpRemoteAddress,
                    matchType = FWP_MATCH_TYPE.FWP_MATCH_EQUAL,
                    conditionValue = new FWP_CONDITION_VALUE0
                    {
                        type = FWP_DATA_TYPE.FWP_V4_ADDR_MASK,
                        value = new FWP_VALUE0_UNION { ptr = v4MaskPtr },
                    },
                },
            ];
            return InstallAppFilter(
                identityPath,
                displayExePath,
                shortFallback,
                LayerConnectRedirectV4,
                new FWPM_ACTION0 { type = FWP_ACTION_TYPE.FWP_ACTION_PERMIT },
                "SVR app loopback ",
                "127.0.0.0/8 PERMIT before redirect callout",
                isCalloutFilter: false,
                role: WfpFilterRole.LoopbackPermitV4,
                weight: WfpVpnAppFilterPlanner.LoopbackPermitFilterWeight,
                extraConditions: extra);
        }
        finally
        {
            if (v4MaskPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(v4MaskPtr);
            }
        }
    }

    private WfpFilterInstallResult InstallIpv6BlockFilter(string identityPath, string displayExePath, bool shortFallback)
        => InstallAppFilter(
            identityPath,
            displayExePath,
            shortFallback,
            LayerAuthConnectV6,
            new FWPM_ACTION0
            {
                type = FWP_ACTION_TYPE.FWP_ACTION_BLOCK,
                value = new FWPM_ACTION0_UNION { filterType = Guid.Empty },
            },
            "SVR IPv6 block ",
            "Prevent IPv6 leak for VPN-routed app",
            isCalloutFilter: false,
            role: WfpFilterRole.Ipv6Block,
            weight: 0,
            extraConditions: null);

    private WfpFilterInstallResult InstallAppFilter(
        string identityPath,
        string displayExePath,
        bool shortFallback,
        Guid layer,
        FWPM_ACTION0 action,
        string namePrefix,
        string description,
        bool isCalloutFilter,
        WfpFilterRole role,
        ulong weight,
        FWPM_FILTER_CONDITION0[]? extraConditions)
    {
        string fileCheckPath = File.Exists(identityPath) ? identityPath : displayExePath;
        if (!File.Exists(fileCheckPath))
        {
            return new WfpFilterInstallResult
            {
                ExePath = displayExePath,
                IdentityPathUsed = identityPath,
                IsShortPathFallback = shortFallback,
                FileExists = false,
                IsCalloutFilter = isCalloutFilter,
                Error = "File not found.",
            };
        }

        if (!WfpAleAppIdBuilder.TryBuildNormalizedForFilePath(identityPath, out WfpOwnedAleAppIdBlob? ownedBlob, out string? buildError))
        {
            return new WfpFilterInstallResult
            {
                ExePath = displayExePath,
                IdentityPathUsed = identityPath,
                IsShortPathFallback = shortFallback,
                FileExists = true,
                AppIdResolved = false,
                IsCalloutFilter = isCalloutFilter,
                Error = buildError ?? "ALE_APP_ID normalization failed.",
            };
        }

        using (ownedBlob)
        {
            var seed = new WfpFilterInstallResult
            {
                ExePath = displayExePath,
                IdentityPathUsed = identityPath,
                IsShortPathFallback = shortFallback,
                FileExists = true,
                AppIdResolved = true,
                AppIdStatus = 0,
                IsCalloutFilter = isCalloutFilter,
                Role = role,
                FilterWeight = weight,
            };

            var conditions = new List<FWPM_FILTER_CONDITION0>
            {
                new()
                {
                    fieldKey = ConditionAleAppId,
                    matchType = FWP_MATCH_TYPE.FWP_MATCH_EQUAL,
                    conditionValue = FWP_CONDITION_VALUE0.FromByteBlobPointer(ownedBlob!.BlobPointer),
                },
            };
            if (extraConditions is { Length: > 0 })
            {
                conditions.AddRange(extraConditions);
            }

            return AddFilter(displayExePath, layer, action, namePrefix, description, conditions, weight, seed);
        }
    }

    private WfpFilterInstallResult AddFilter(
        string displayExePath,
        Guid layer,
        FWPM_ACTION0 action,
        string namePrefix,
        string description,
        IReadOnlyList<FWPM_FILTER_CONDITION0> conditions,
        ulong weightValue,
        WfpFilterInstallResult seed)
    {
        Guid filterKey = Guid.NewGuid();
        IntPtr condBlock = IntPtr.Zero;
        IntPtr providerKeyPtr = IntPtr.Zero;
        IntPtr filterMem = IntPtr.Zero;
        IntPtr weightPtr = IntPtr.Zero;
        try
        {
            WfpAppIdConditionValidation validation = WfpAppIdConditionDiagnostics.ValidateAppIdCondition(
                ConditionAleAppId,
                (uint)FWP_MATCH_TYPE.FWP_MATCH_EQUAL,
                (uint)FWP_DATA_TYPE.FWP_BYTE_BLOB_TYPE,
                conditions[0].conditionValue.value.byteBlob);
            if (!validation.IsValid)
            {
                return seed with
                {
                    FilterInstalled = false,
                    FilterAddStatus = 0,
                    Error = validation.Error + " " + validation.DiagnosticLine,
                };
            }

            int condSize = Marshal.SizeOf<FWPM_FILTER_CONDITION0>();
            condBlock = Marshal.AllocHGlobal(condSize * conditions.Count);
            for (int i = 0; i < conditions.Count; i++)
            {
                Marshal.StructureToPtr(conditions[i], IntPtr.Add(condBlock, i * condSize), false);
            }

            providerKeyPtr = Marshal.AllocHGlobal(Marshal.SizeOf<Guid>());
            Marshal.StructureToPtr(ProviderKey, providerKeyPtr, false);

            FWP_VALUE0 weight = FWP_VALUE0.Empty;
            if (weightValue > 0)
            {
                weightPtr = Marshal.AllocHGlobal(8);
                Marshal.WriteInt64(weightPtr, (long)weightValue);
                weight = new FWP_VALUE0
                {
                    type = FWP_DATA_TYPE.FWP_UINT64,
                    value = new FWP_VALUE0_UNION { uint64 = weightPtr },
                };
            }

            var filter = default(FWPM_FILTER0);
            filter.filterKey = filterKey;
            filter.displayData = new FWPM_DISPLAY_DATA0
            {
                name = namePrefix + Path.GetFileName(displayExePath),
                description = description,
            };
            filter.providerKey = providerKeyPtr;
            filter.layerKey = layer;
            filter.subLayerKey = SublayerKey;
            filter.action = action;
            filter.numFilterConditions = (uint)conditions.Count;
            filter.filterCondition = condBlock;
            filter.weight = weight;

            uint actionType = (uint)action.type;
            string addContext = WfpActionDiagnostics.FormatFilterAddContext(
                actionType,
                layer,
                SublayerKey,
                weightValue,
                seed.Role);

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
                : "FwpmFilterAdd0 failed: " + WfpNativeStatus.Describe(filterAddStatus) + " " + addContext + " " + validation.DiagnosticLine;

            return seed with
            {
                AppIdResolved = true,
                FilterInstalled = installed,
                FilterAddStatus = filterAddStatus,
                FilterId = filterId,
                ActionType = actionType,
                FilterAddContext = addContext,
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

            if (condBlock != IntPtr.Zero)
            {
                for (int i = 0; i < conditions.Count; i++)
                {
                    Marshal.DestroyStructure<FWPM_FILTER_CONDITION0>(IntPtr.Add(condBlock, i * Marshal.SizeOf<FWPM_FILTER_CONDITION0>()));
                }

                Marshal.FreeHGlobal(condBlock);
            }

            if (weightPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(weightPtr);
            }

            if (providerKeyPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(providerKeyPtr);
            }
        }
    }
}

internal static class WfpRuntimeCaptureConditionLayout
{
    public static string SummaryLine
    {
        get
        {
            int condSize = Marshal.SizeOf<FWPM_FILTER_CONDITION0>();
            int fieldKey = (int)Marshal.OffsetOf<FWPM_FILTER_CONDITION0>("fieldKey");
            int matchType = (int)Marshal.OffsetOf<FWPM_FILTER_CONDITION0>("matchType");
            int conditionValue = (int)Marshal.OffsetOf<FWPM_FILTER_CONDITION0>("conditionValue");
            return "FWPM_FILTER_CONDITION0 sizeof=" + condSize
                + " stride=" + condSize
                + " offsets fieldKey=" + fieldKey
                + " matchType=" + matchType
                + " conditionValue=" + conditionValue;
        }
    }

    public static IReadOnlyList<string> DescribeConditions(FWPM_FILTER_CONDITION0[] conditions)
    {
        var lines = new List<string>(conditions.Length);
        for (int i = 0; i < conditions.Length; i++)
        {
            FWPM_FILTER_CONDITION0 c = conditions[i];
            lines.Add(
                "index=" + i
                + " fieldKey=" + c.fieldKey
                + " matchType=" + (uint)c.matchType
                + " conditionValue.type=" + (uint)c.conditionValue.type);
        }

        return lines;
    }
}

public readonly record struct WfpRuntimeCaptureFilterSetup(
    Guid LayerKey,
    Guid SubLayerKey,
    Guid CalloutKey,
    ulong RawContext,
    uint ActionType,
    string WeightDescription);

public readonly record struct WfpRuntimeCaptureFilterResult(
    uint FilterAddStatus,
    ulong FilterId,
    Guid FilterKey,
    string? Error)
{
    public bool Success => FilterAddStatus == 0 && FilterId != 0 && string.IsNullOrEmpty(Error);
}

public interface IWfpRuntimeCaptureFilterInstaller
{
    bool SessionOpen { get; }

    bool TryAddRuntimeAppIdCaptureFilter(out WfpRuntimeCaptureFilterResult result);

    void RemoveRuntimeAppIdCaptureFilter(Guid filterKey);
}

public static class WfpRuntimeCaptureFilterPolicy
{
    public static readonly IReadOnlyList<string> ForbiddenUserModeOperations =
    [
        "FwpmEngineOpen0",
        "FwpmEngineClose0",
        "FwpmProviderAdd0",
        "FwpmSubLayerAdd0",
        "FwpmCalloutAdd0",
    ];

    public const bool RequiresProductionWfpSession = true;
}

public sealed class WfpRuntimeAppIdCapture : IDisposable
{
    public const ushort DiagnosticRemotePort = 39547;
    public const string DiagnosticRemoteHost = "198.51.100.1";
    public const ulong FilterRawContext = 0x535652444931UL;

    private readonly IWfpRuntimeCaptureFilterInstaller _installer;
    private Guid _filterKey;
    private bool _installed;
    private WfpRuntimeCaptureFilterResult _installResult;

    public WfpRuntimeAppIdCapture(IWfpRuntimeCaptureFilterInstaller installer)
    {
        _installer = installer;
    }

    public bool ReusedProductionWfpSession => _installed;

    public uint FilterAddStatus => _installResult.FilterAddStatus;

    public ulong DiagnosticFilterId => _installResult.FilterId;

    /// <summary>FWPM_CONDITION_IP_REMOTE_ADDRESS value (network-order uint32).</summary>
    public static uint DiagnosticRemoteAddressUInt32 =>
        WfpIpv4AddressEncoding.ToWfpIpv4AddressUInt32(DiagnosticRemoteHost);

    public static byte[] DiagnosticRemoteAddressBytes =>
        IPAddress.Parse(DiagnosticRemoteHost).GetAddressBytes();

    /// <summary>Legacy incorrect encoding (host little-endian); A/B/C regression only.</summary>
    public static uint DiagnosticRemoteAddressUInt32HostLittleEndian =>
        BitConverter.ToUInt32(DiagnosticRemoteAddressBytes, 0);

    public static uint DiagnosticRemoteAddressUInt32NetworkOrder =>
        WfpIpv4AddressEncoding.ToWfpIpv4AddressUInt32(DiagnosticRemoteHost);

    public static string FormatRemoteAddressEncodingReport()
    {
        byte[] b = DiagnosticRemoteAddressBytes;
        string hexBytes = string.Join(" ", b.Select(x => x.ToString("X2")));
        return "diagnosticRemoteAddress=" + DiagnosticRemoteHost
            + " | diagnosticRemoteAddressUInt32=0x" + DiagnosticRemoteAddressUInt32.ToString("X8")
            + " | addressEncoding=network-order"
            + " | rawBytes=" + hexBytes
            + " | hostLE(wrongForWfp)=0x" + DiagnosticRemoteAddressUInt32HostLittleEndian.ToString("X8")
            + " | helper=WfpIpv4AddressEncoding.ToWfpIpv4AddressUInt32";
    }

    public bool TryInstall(out string error)
    {
        error = "";
        if (_installed)
        {
            return true;
        }

        if (!OperatingSystem.IsWindows())
        {
            error = "Windows only.";
            return false;
        }

        if (!_installer.SessionOpen)
        {
            error = "Production WFP session is not open.";
            return false;
        }

        if (!_installer.TryAddRuntimeAppIdCaptureFilter(out WfpRuntimeCaptureFilterResult result))
        {
            _installResult = result;
            error = result.Error ?? "FwpmFilterAdd0 failed: 0x" + result.FilterAddStatus.ToString("X8");
            return false;
        }

        _installResult = result;
        _filterKey = result.FilterKey;
        _installed = true;
        return true;
    }

    public void Dispose()
    {
        if (_installed && _filterKey != Guid.Empty)
        {
            _installer.RemoveRuntimeAppIdCaptureFilter(_filterKey);
        }

        _installed = false;
        _filterKey = Guid.Empty;
        _installResult = default;
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
