using System.Runtime.InteropServices;
using static SelectiveVpnRouter.Network.WfpNativeTypes;

namespace SelectiveVpnRouter.Network;

public sealed record WfpAppIdSmokeResult(bool Success, uint FilterAddStatus, ulong FilterId, string DiagnosticLine, string? Error);

public static class WfpAppIdSmokeTest
{
    public static WfpAppIdSmokeResult Run(string? exePath = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new WfpAppIdSmokeResult(false, 0, 0, string.Empty, "Windows only.");
        }

        exePath ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe");
        if (!File.Exists(exePath))
        {
            return new WfpAppIdSmokeResult(false, 0, 0, string.Empty, "Executable not found: " + exePath);
        }

        IntPtr engine = IntPtr.Zero;
        IntPtr appIdPtr = IntPtr.Zero;
        IntPtr condMem = IntPtr.Zero;
        IntPtr providerKeyPtr = IntPtr.Zero;
        IntPtr filterMem = IntPtr.Zero;
        Guid providerKey = Guid.NewGuid();
        Guid subLayerKey = Guid.NewGuid();
        Guid filterKey = Guid.NewGuid();

        try
        {
            var session = new FWPM_SESSION0 { flags = 1 };
            uint st = Native.FwpmEngineOpen0(null, 10, IntPtr.Zero, ref session, out engine);
            if (st != 0)
            {
                return new WfpAppIdSmokeResult(false, st, 0, string.Empty, "FwpmEngineOpen0 0x" + st.ToString("X"));
            }

            var provider = new FWPM_PROVIDER0
            {
                providerKey = providerKey,
                displayData = new FWPM_DISPLAY_DATA0 { name = "SVR app-id smoke", description = "temporary" },
            };
            st = Native.FwpmProviderAdd0(engine, ref provider, IntPtr.Zero);
            if (st != 0 && st != 0x80320016)
            {
                return new WfpAppIdSmokeResult(false, st, 0, string.Empty, "FwpmProviderAdd0 0x" + st.ToString("X"));
            }

            providerKeyPtr = Marshal.AllocHGlobal(Marshal.SizeOf<Guid>());
            Marshal.StructureToPtr(providerKey, providerKeyPtr, false);

            var sub = new FWPM_SUBLAYER0
            {
                subLayerKey = subLayerKey,
                displayData = new FWPM_DISPLAY_DATA0 { name = "SVR app-id smoke sub", description = "" },
                providerKey = providerKeyPtr,
                weight = 0x7FFF,
            };
            st = Native.FwpmSubLayerAdd0(engine, ref sub, IntPtr.Zero);
            if (st != 0 && st != 0x80320016)
            {
                return new WfpAppIdSmokeResult(false, st, 0, string.Empty, "FwpmSubLayerAdd0 0x" + st.ToString("X"));
            }

            st = Native.FwpmGetAppIdFromFileName0(exePath, out appIdPtr);
            if (st != 0 || appIdPtr == IntPtr.Zero)
            {
                return new WfpAppIdSmokeResult(false, st, 0, string.Empty, "FwpmGetAppIdFromFileName0 0x" + st.ToString("X"));
            }

            var conditionValue = FWP_CONDITION_VALUE0.FromByteBlobPointer(appIdPtr);
            WfpAppIdConditionValidation validation = WfpAppIdConditionDiagnostics.ValidateAppIdCondition(
                WfpConstants.ConditionAleAppId,
                (uint)FWP_MATCH_TYPE.FWP_MATCH_EQUAL,
                (uint)conditionValue.type,
                appIdPtr);
            if (!validation.IsValid)
            {
                return new WfpAppIdSmokeResult(false, 0, 0, validation.DiagnosticLine, validation.Error);
            }

            var cond = new FWPM_FILTER_CONDITION0
            {
                fieldKey = WfpConstants.ConditionAleAppId,
                matchType = FWP_MATCH_TYPE.FWP_MATCH_EQUAL,
                conditionValue = conditionValue,
            };
            condMem = Marshal.AllocHGlobal(Marshal.SizeOf<FWPM_FILTER_CONDITION0>());
            Marshal.StructureToPtr(cond, condMem, false);

            var filter = default(FWPM_FILTER0);
            filter.filterKey = filterKey;
            filter.displayData = new FWPM_DISPLAY_DATA0 { name = "SVR app-id smoke filter", description = "temporary BLOCK" };
            filter.providerKey = providerKeyPtr;
            filter.layerKey = WfpConstants.LayerAleAuthConnectV6;
            filter.subLayerKey = subLayerKey;
            filter.weight = FWP_VALUE0.Empty;
            filter.numFilterConditions = 1;
            filter.filterCondition = condMem;
            filter.action = new FWPM_ACTION0
            {
                type = FWP_ACTION_TYPE.FWP_ACTION_BLOCK,
                value = new FWPM_ACTION0_UNION { filterType = Guid.Empty },
            };

            filterMem = Marshal.AllocHGlobal(Marshal.SizeOf<FWPM_FILTER0>());
            Marshal.StructureToPtr(filter, filterMem, false);

            st = Native.FwpmFilterAdd0(engine, filterMem, IntPtr.Zero, out ulong filterId);
            bool ok = st == 0 && filterId != 0;
            if (ok)
            {
                Native.FwpmFilterDeleteByKey0(engine, ref filterKey);
            }

            return new WfpAppIdSmokeResult(
                ok,
                st,
                filterId,
                validation.DiagnosticLine,
                ok ? null : "FwpmFilterAdd0 failed: " + WfpNativeStatus.Describe(st));
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

            if (appIdPtr != IntPtr.Zero)
            {
                Native.FwpmFreeMemory0(ref appIdPtr);
            }

            if (engine != IntPtr.Zero)
            {
                Native.FwpmEngineClose0(engine);
            }
        }
    }
}