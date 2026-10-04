using System.Runtime.InteropServices;
using SelectiveVpnRouter.Core;

namespace SelectiveVpnRouter.Network;

/// <summary>
/// WFP management structures matching Windows SDK fwptypes.h / fwpmtypes.h (x64).
/// Layout verified against tools/wfp-abi-probe/wfp_abi_probe.exe (Windows SDK 10.0.26100.0).
/// </summary>
internal static class WfpNativeTypes
{
    // Exact numeric values from fwptypes.h — do not auto-increment past FWP_SINGLE_DATA_TYPE_MAX.
    internal enum FWP_DATA_TYPE : uint
    {
        FWP_EMPTY = 0,
        FWP_UINT8 = 1,
        FWP_UINT16 = 2,
        FWP_UINT32 = 3,
        FWP_UINT64 = 4,
        FWP_INT8 = 5,
        FWP_INT16 = 6,
        FWP_INT32 = 7,
        FWP_INT64 = 8,
        FWP_FLOAT = 9,
        FWP_DOUBLE = 10,
        FWP_BYTE_ARRAY16_TYPE = 11,
        FWP_BYTE_BLOB_TYPE = 12,
        FWP_SID = 13,
        FWP_SECURITY_DESCRIPTOR_TYPE = 14,
        FWP_TOKEN_INFORMATION_TYPE = 15,
        FWP_TOKEN_ACCESS_INFORMATION_TYPE = 16,
        FWP_UNICODE_STRING_TYPE = 17,
        FWP_BYTE_ARRAY6_TYPE = 18,
        FWP_SINGLE_DATA_TYPE_MAX = 0xFF,
        FWP_V4_ADDR_MASK = 0x100,
        FWP_V6_ADDR_MASK = 0x101,
        FWP_RANGE_TYPE = 0x102,
    }

    internal enum FWP_MATCH_TYPE : uint
    {
        FWP_MATCH_EQUAL = 0,
    }

    internal enum FWP_ACTION_TYPE : uint
    {
        FWP_ACTION_BLOCK = WfpActionConstants.FwpActionBlock,
        FWP_ACTION_PERMIT = WfpActionConstants.FwpActionPermit,
        FWP_ACTION_CALLOUT_UNKNOWN = WfpActionConstants.FwpActionCalloutUnknown,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FWP_V4_ADDR_AND_MASK0
    {
        public uint addr;
        public uint mask;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct FWPM_DISPLAY_DATA0
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string name;
        [MarshalAs(UnmanagedType.LPWStr)] public string description;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FWP_BYTE_BLOB
    {
        public uint size;
        public IntPtr data;
    }

    [StructLayout(LayoutKind.Explicit, Size = 8)]
    internal struct FWP_VALUE0_UNION
    {
        [FieldOffset(0)] public byte uint8;
        [FieldOffset(0)] public ushort uint16;
        [FieldOffset(0)] public uint uint32;
        [FieldOffset(0)] public IntPtr uint64;
        [FieldOffset(0)] public IntPtr int64;
        [FieldOffset(0)] public IntPtr byteBlob;
        [FieldOffset(0)] public IntPtr sid;
        [FieldOffset(0)] public IntPtr ptr;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FWP_VALUE0
    {
        public FWP_DATA_TYPE type;
        public FWP_VALUE0_UNION value;

        public static FWP_VALUE0 Empty => default;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FWP_CONDITION_VALUE0
    {
        public FWP_DATA_TYPE type;
        public FWP_VALUE0_UNION value;

        public static FWP_CONDITION_VALUE0 FromByteBlobPointer(IntPtr appIdBlobPtr) =>
            new()
            {
                type = FWP_DATA_TYPE.FWP_BYTE_BLOB_TYPE,
                value = new FWP_VALUE0_UNION { byteBlob = appIdBlobPtr },
            };
    }

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    internal struct FWPM_FILTER_CONDITION0
    {
        [FieldOffset(0)] public Guid fieldKey;
        [FieldOffset(16)] public FWP_MATCH_TYPE matchType;
        [FieldOffset(24)] public FWP_CONDITION_VALUE0 conditionValue;
    }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    internal struct FWPM_ACTION0_UNION
    {
        [FieldOffset(0)] public Guid filterType;
        [FieldOffset(0)] public Guid calloutKey;
    }

    [StructLayout(LayoutKind.Sequential, Size = 20)]
    internal struct FWPM_ACTION0
    {
        public FWP_ACTION_TYPE type;
        public FWPM_ACTION0_UNION value;
    }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    internal struct FWPM_FILTER_CONTEXT0
    {
        [FieldOffset(0)] public ulong rawContext;
        [FieldOffset(0)] public Guid providerContextKey;
    }

    [StructLayout(LayoutKind.Explicit, Size = 200)]
    internal struct FWPM_FILTER0
    {
        [FieldOffset(0)] public Guid filterKey;
        [FieldOffset(16)] public FWPM_DISPLAY_DATA0 displayData;
        [FieldOffset(32)] public uint flags;
        [FieldOffset(40)] public IntPtr providerKey;
        [FieldOffset(48)] public FWP_BYTE_BLOB providerData;
        [FieldOffset(64)] public Guid layerKey;
        [FieldOffset(80)] public Guid subLayerKey;
        [FieldOffset(96)] public FWP_VALUE0 weight;
        [FieldOffset(112)] public uint numFilterConditions;
        [FieldOffset(120)] public IntPtr filterCondition;
        [FieldOffset(128)] public FWPM_ACTION0 action;
        [FieldOffset(152)] public FWPM_FILTER_CONTEXT0 context;
        [FieldOffset(168)] public IntPtr reserved;
        [FieldOffset(176)] public ulong filterId;
        [FieldOffset(184)] public FWP_VALUE0 effectiveWeight;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FWPM_SESSION0
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
    internal struct FWPM_PROVIDER0
    {
        public Guid providerKey;
        public FWPM_DISPLAY_DATA0 displayData;
        public uint flags;
        public FWP_BYTE_BLOB providerData;
        [MarshalAs(UnmanagedType.LPWStr)] public string? serviceName;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FWPM_SUBLAYER0
    {
        public Guid subLayerKey;
        public FWPM_DISPLAY_DATA0 displayData;
        public uint flags;
        public IntPtr providerKey;
        public FWP_BYTE_BLOB providerData;
        public ushort weight;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FWPM_CALLOUT0
    {
        public Guid calloutKey;
        public FWPM_DISPLAY_DATA0 displayData;
        public uint flags;
        public IntPtr providerKey;
        public FWP_BYTE_BLOB providerData;
        public Guid applicableLayer;
        public uint calloutId;
    }

    internal static class Native
    {
        [DllImport("fwpuclnt.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Winapi)]
        public static extern uint FwpmEngineOpen0(string? serverName, uint authnService, IntPtr authIdentity, ref FWPM_SESSION0 session, out IntPtr engine);

        [DllImport("fwpuclnt.dll", CallingConvention = CallingConvention.Winapi)]
        public static extern uint FwpmEngineClose0(IntPtr engine);

        [DllImport("fwpuclnt.dll", CallingConvention = CallingConvention.Winapi)]
        public static extern uint FwpmProviderAdd0(IntPtr engine, ref FWPM_PROVIDER0 provider, IntPtr sd);

        [DllImport("fwpuclnt.dll", CallingConvention = CallingConvention.Winapi)]
        public static extern uint FwpmSubLayerAdd0(IntPtr engine, ref FWPM_SUBLAYER0 sub, IntPtr sd);

        [DllImport("fwpuclnt.dll", CallingConvention = CallingConvention.Winapi)]
        public static extern uint FwpmCalloutAdd0(IntPtr engine, ref FWPM_CALLOUT0 callout, IntPtr sd, out uint id);

        [DllImport("fwpuclnt.dll", CallingConvention = CallingConvention.Winapi)]
        public static extern uint FwpmFilterAdd0(IntPtr engine, IntPtr filter, IntPtr sd, out ulong id);

        [DllImport("fwpuclnt.dll", CallingConvention = CallingConvention.Winapi)]
        public static extern uint FwpmFilterDeleteByKey0(IntPtr engine, ref Guid key);

        [DllImport("fwpuclnt.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Winapi)]
        public static extern uint FwpmGetAppIdFromFileName0(string fileName, out IntPtr appId);

        [DllImport("fwpuclnt.dll", CallingConvention = CallingConvention.Winapi)]
        public static extern void FwpmFreeMemory0(ref IntPtr p);
    }
}