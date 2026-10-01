using System.Runtime.InteropServices;
using SelectiveVpnRouter.Core;

namespace SelectiveVpnRouter.Network;

public sealed class CalloutDriverClient : IDisposable
{
    static CalloutDriverClient()
    {
        if (OperatingSystem.IsWindows())
        {
            CalloutDriverStatusAbiVerification.ThrowIfMismatch();
        }
    }

    public const string DevicePath = @"\\.\SelectiveVpnCallout";
    private const uint IoctlSetTarget = 0x00222004; // CTL_CODE(FILE_DEVICE_UNKNOWN, 0x801, METHOD_BUFFERED, FILE_ANY_ACCESS)
    private const uint IoctlGetStatus = 0x00222008; // function 0x802
    private const uint IoctlResetRuntimeCapture = 0x0022200C; // function 0x803
    public const uint ExpectedStatusStructVersion = 2;
    public const int RuntimeAppIdTextCharCount = 512;

    private IntPtr _handle = new(-1);

    public bool IsLoaded => _handle != IntPtr.Zero && _handle != new IntPtr(-1);

    public static CalloutDriverClient TryOpen()
    {
        var client = new CalloutDriverClient();
        client._handle = Native.CreateFile(
            DevicePath,
            Native.GenericRead | Native.GenericWrite,
            0,
            IntPtr.Zero,
            Native.OpenExisting,
            0,
            IntPtr.Zero);
        return client;
    }

    public bool TrySetRedirectTarget(int proxyPid, ushort proxyPort, out string error)
    {
        error = "";
        if (!IsLoaded)
        {
            error = "Callout driver is not loaded.";
            return false;
        }

        var buf = new TargetBuffer { ProxyPid = (uint)proxyPid, ProxyPort = proxyPort, Enabled = 1 };
        return SendTarget(buf, out error);
    }

    public bool TryDisable(out string error)
    {
        error = "";
        if (!IsLoaded)
        {
            return true;
        }

        return SendTarget(new TargetBuffer { Enabled = 0 }, out error);
    }

    public bool TryResetRuntimeCapture(out string error)
    {
        error = "";
        if (!IsLoaded)
        {
            error = "Callout driver is not loaded.";
            return false;
        }

        if (!Native.DeviceIoControl(_handle, IoctlResetRuntimeCapture, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero))
        {
            error = "DeviceIoControl RESET_RUNTIME_CAPTURE failed " + Marshal.GetLastWin32Error();
            return false;
        }

        return true;
    }

    public bool TryGetStatus(out CalloutArmStatus status, out string error)
    {
        status = new CalloutArmStatus();
        error = "";
        if (!IsLoaded)
        {
            error = "Callout driver is not loaded.";
            return false;
        }

        int size = Marshal.SizeOf<CalloutStatusBuffer>();
        IntPtr p = Marshal.AllocHGlobal(size);
        try
        {
            if (!Native.DeviceIoControl(_handle, IoctlGetStatus, IntPtr.Zero, 0, p, (uint)size, out _, IntPtr.Zero))
            {
                error = "DeviceIoControl GET_STATUS failed " + Marshal.GetLastWin32Error();
                return false;
            }

            var buf = Marshal.PtrToStructure<CalloutStatusBuffer>(p);
            string runtimeAppId = "";
            if (buf.StatusPad == ExpectedStatusStructVersion)
            {
                runtimeAppId = new string(buf.RuntimeAppIdText).TrimEnd('\0');
            }

            status = new CalloutArmStatus
            {
                DeviceOpen = true,
                Enabled = buf.Enabled != 0,
                ProxyPid = buf.ProxyPid,
                ProxyPort = buf.ProxyPort,
                CalloutId = buf.CalloutId,
                OpenHandles = buf.OpenHandles,
                Redirects = buf.Redirects,
                RedirectAttempts = buf.RedirectAttempts,
                RedirectApplySuccess = buf.RedirectApplySuccess,
                RedirectApplyFailures = buf.RedirectApplyFailures,
                LastRedirectApplyStatus = buf.LastRedirectApplyStatus,
                ClassifyEntries = buf.ClassifyEntries,
                ExitNoActionWrite = buf.ExitNoActionWrite,
                ExitDisabled = buf.ExitDisabled,
                ExitProxyPidZero = buf.ExitProxyPidZero,
                ExitProxyPortZero = buf.ExitProxyPortZero,
                ExitRedirectHandleNull = buf.ExitRedirectHandleNull,
                ExitClassifyContextNull = buf.ExitClassifyContextNull,
                ExitPidZero = buf.ExitPidZero,
                ExitProxyPid = buf.ExitProxyPid,
                AcquireClassifyHandleFailures = buf.AcquireClassifyHandleFailures,
                AcquireWritableLayerDataFailures = buf.AcquireWritableLayerDataFailures,
                AlreadyLoopbackProxy = buf.AlreadyLoopbackProxy,
                AllocationFailures = buf.AllocationFailures,
                LastClassifyPid = buf.LastClassifyPid,
                LastFilterId = buf.LastFilterId,
                LastRights = buf.LastRights,
                StatusStructVersion = buf.StatusPad,
                RuntimeCaptureCount = buf.RuntimeCaptureCount,
                RuntimeAppIdPresent = buf.RuntimeAppIdPresent != 0,
                RuntimeAppIdByteLength = buf.RuntimeAppIdByteLength,
                RuntimeAppIdValueType = buf.RuntimeAppIdValueType,
                RuntimeProcessId = buf.RuntimeProcessId,
                RuntimeFilterId = buf.RuntimeFilterId,
                RuntimeRights = buf.RuntimeRights,
                RuntimeAppId = runtimeAppId,
            };
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(p);
        }
    }

    public void Dispose()
    {
        if (IsLoaded)
        {
            try { TryDisable(out _); } catch (Exception) { }
            Native.CloseHandle(_handle);
            _handle = new IntPtr(-1);
        }
    }

    private bool SendTarget(TargetBuffer buf, out string error)
    {
        error = "";
        int size = Marshal.SizeOf<TargetBuffer>();
        IntPtr p = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(buf, p, false);
            if (!Native.DeviceIoControl(_handle, IoctlSetTarget, p, (uint)size, IntPtr.Zero, 0, out _, IntPtr.Zero))
            {
                error = "DeviceIoControl failed " + Marshal.GetLastWin32Error();
                return false;
            }

            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(p);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TargetBuffer
    {
        public uint ProxyPid;
        public ushort ProxyPort;
        public ushort Enabled;
    }

    private static class Native
    {
        public const uint GenericRead = 0x80000000;
        public const uint GenericWrite = 0x40000000;
        public const uint OpenExisting = 3;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr sa, uint disp, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool DeviceIoControl(IntPtr h, uint code, IntPtr inBuf, uint inSize, IntPtr outBuf, uint outSize, out uint returned, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr h);
    }
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct CalloutStatusBuffer
{
    public uint ProxyPid;
    public ushort ProxyPort;
    public ushort Enabled;
    public uint CalloutId;
    public uint OpenHandles;
    public uint Redirects;
    public uint RedirectAttempts;
    public uint RedirectApplySuccess;
    public uint RedirectApplyFailures;
    public int LastRedirectApplyStatus;
    public uint ClassifyEntries;
    public uint ExitNoActionWrite;
    public uint ExitDisabled;
    public uint ExitProxyPidZero;
    public uint ExitProxyPortZero;
    public uint ExitRedirectHandleNull;
    public uint ExitClassifyContextNull;
    public uint ExitPidZero;
    public uint ExitProxyPid;
    public uint AcquireClassifyHandleFailures;
    public uint AcquireWritableLayerDataFailures;
    public uint AlreadyLoopbackProxy;
    public uint AllocationFailures;
    public ulong LastClassifyPid;
    public ulong LastFilterId;
    public uint LastRights;
    public uint StatusPad;
    public uint RuntimeCaptureCount;
    public uint RuntimeAppIdPresent;
    public uint RuntimeAppIdByteLength;
    public uint RuntimeAppIdValueType;
    public ulong RuntimeProcessId;
    public ulong RuntimeFilterId;
    public uint RuntimeRights;
    public uint RuntimeCapturePad;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CalloutDriverClient.RuntimeAppIdTextCharCount)]
    public string RuntimeAppIdText;
}

[StructLayout(LayoutKind.Sequential)]
internal struct LegacyCalloutStatusBufferWithoutUnicodeCharset
{
    public uint ProxyPid;
    public ushort ProxyPort;
    public ushort Enabled;
    public uint CalloutId;
    public uint OpenHandles;
    public uint Redirects;
    public uint RedirectAttempts;
    public uint RedirectApplySuccess;
    public uint RedirectApplyFailures;
    public int LastRedirectApplyStatus;
    public uint ClassifyEntries;
    public uint ExitNoActionWrite;
    public uint ExitDisabled;
    public uint ExitProxyPidZero;
    public uint ExitProxyPortZero;
    public uint ExitRedirectHandleNull;
    public uint ExitClassifyContextNull;
    public uint ExitPidZero;
    public uint ExitProxyPid;
    public uint AcquireClassifyHandleFailures;
    public uint AcquireWritableLayerDataFailures;
    public uint AlreadyLoopbackProxy;
    public uint AllocationFailures;
    public ulong LastClassifyPid;
    public ulong LastFilterId;
    public uint LastRights;
    public uint StatusPad;
    public uint RuntimeCaptureCount;
    public uint RuntimeAppIdPresent;
    public uint RuntimeAppIdByteLength;
    public uint RuntimeAppIdValueType;
    public ulong RuntimeProcessId;
    public ulong RuntimeFilterId;
    public uint RuntimeRights;
    public uint RuntimeCapturePad;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CalloutDriverClient.RuntimeAppIdTextCharCount)]
    public string RuntimeAppIdText;
}

public static class CalloutDriverStatusAbiVerification
{
    public const int LegacyIncorrectManagedSizeBytes = 664;
    public const int ExpectedNativeStatusSizeBytes = 1176;
    public const int RuntimeAppIdTextCharCount = CalloutDriverClient.RuntimeAppIdTextCharCount;
    public const int RuntimeAppIdTextByteLength = RuntimeAppIdTextCharCount * 2;

    public sealed record LayoutMismatch(string Field, int ExpectedOffset, int ActualOffset);

    public static int ManagedStatusBufferSize => Marshal.SizeOf<CalloutStatusBuffer>();

    public static int MeasuredLegacyIncorrectManagedSizeBytes =>
        Marshal.SizeOf<LegacyCalloutStatusBufferWithoutUnicodeCharset>();

    public static IReadOnlyDictionary<string, int> ExpectedFieldOffsets { get; } =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["ProxyPid"] = 0,
            ["ProxyPort"] = 4,
            ["Enabled"] = 6,
            ["CalloutId"] = 8,
            ["OpenHandles"] = 12,
            ["Redirects"] = 16,
            ["RedirectAttempts"] = 20,
            ["RedirectApplySuccess"] = 24,
            ["RedirectApplyFailures"] = 28,
            ["LastRedirectApplyStatus"] = 32,
            ["ClassifyEntries"] = 36,
            ["ExitNoActionWrite"] = 40,
            ["ExitDisabled"] = 44,
            ["ExitProxyPidZero"] = 48,
            ["ExitProxyPortZero"] = 52,
            ["ExitRedirectHandleNull"] = 56,
            ["ExitClassifyContextNull"] = 60,
            ["ExitPidZero"] = 64,
            ["ExitProxyPid"] = 68,
            ["AcquireClassifyHandleFailures"] = 72,
            ["AcquireWritableLayerDataFailures"] = 76,
            ["AlreadyLoopbackProxy"] = 80,
            ["AllocationFailures"] = 84,
            ["LastClassifyPid"] = 88,
            ["LastFilterId"] = 96,
            ["LastRights"] = 104,
            ["StatusPad"] = 108,
            ["RuntimeCaptureCount"] = 112,
            ["RuntimeAppIdPresent"] = 116,
            ["RuntimeAppIdByteLength"] = 120,
            ["RuntimeAppIdValueType"] = 124,
            ["RuntimeProcessId"] = 128,
            ["RuntimeFilterId"] = 136,
            ["RuntimeRights"] = 144,
            ["RuntimeCapturePad"] = 148,
            ["RuntimeAppIdText"] = 152,
        };

    public static IReadOnlyList<LayoutMismatch> CompareManagedToNative()
    {
        var mismatches = new List<LayoutMismatch>();

        if (ManagedStatusBufferSize != ExpectedNativeStatusSizeBytes)
        {
            mismatches.Add(new LayoutMismatch("(sizeof)", ExpectedNativeStatusSizeBytes, ManagedStatusBufferSize));
        }

        if (MeasuredLegacyIncorrectManagedSizeBytes != LegacyIncorrectManagedSizeBytes)
        {
            mismatches.Add(new LayoutMismatch(
                "(legacy sizeof)",
                LegacyIncorrectManagedSizeBytes,
                MeasuredLegacyIncorrectManagedSizeBytes));
        }

        foreach ((string field, int expected) in ExpectedFieldOffsets)
        {
            int actual = (int)Marshal.OffsetOf<CalloutStatusBuffer>(field);
            if (actual != expected)
            {
                mismatches.Add(new LayoutMismatch(field, expected, actual));
            }
        }

        return mismatches;
    }

    public static void ThrowIfMismatch()
    {
        IReadOnlyList<LayoutMismatch> mismatches = CompareManagedToNative();
        if (mismatches.Count == 0)
        {
            return;
        }

        string details = string.Join(
            "; ",
            mismatches.Select(m => $"{m.Field}: expected {m.ExpectedOffset}, got {m.ActualOffset}"));
        throw new InvalidOperationException("SVR_STATUS ABI layout mismatch vs driver: " + details);
    }
}
