using System.Runtime.InteropServices;
using SelectiveVpnRouter.Core;

namespace SelectiveVpnRouter.Network;

public sealed class CalloutDriverClient : IDisposable
{
    public const string DevicePath = @"\\.\SelectiveVpnCallout";
    private const uint IoctlSetTarget = 0x00222004; // CTL_CODE(FILE_DEVICE_UNKNOWN, 0x801, METHOD_BUFFERED, FILE_ANY_ACCESS)
    private const uint IoctlGetStatus = 0x00222008; // function 0x802

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

    public bool TryGetStatus(out CalloutArmStatus status, out string error)
    {
        status = new CalloutArmStatus();
        error = "";
        if (!IsLoaded)
        {
            error = "Callout driver is not loaded.";
            return false;
        }

        int size = Marshal.SizeOf<StatusBuffer>();
        IntPtr p = Marshal.AllocHGlobal(size);
        try
        {
            if (!Native.DeviceIoControl(_handle, IoctlGetStatus, IntPtr.Zero, 0, p, (uint)size, out _, IntPtr.Zero))
            {
                error = "DeviceIoControl GET_STATUS failed " + Marshal.GetLastWin32Error();
                return false;
            }

            var buf = Marshal.PtrToStructure<StatusBuffer>(p);
            status = new CalloutArmStatus
            {
                DeviceOpen = true,
                Enabled = buf.Enabled != 0,
                ProxyPid = buf.ProxyPid,
                ProxyPort = buf.ProxyPort,
                CalloutId = buf.CalloutId,
                OpenHandles = buf.OpenHandles,
                Redirects = buf.Redirects,
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

    [StructLayout(LayoutKind.Sequential)]
    private struct StatusBuffer
    {
        public uint ProxyPid;
        public ushort ProxyPort;
        public ushort Enabled;
        public uint CalloutId;
        public uint OpenHandles;
        public uint Redirects;
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
