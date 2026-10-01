using System.Runtime.InteropServices;
using SelectiveVpnRouter.Core;

namespace SelectiveVpnRouter.Network;

/// <summary>
/// Resolves executable paths into one or more WFP ALE_APP_ID identity paths.
/// </summary>
public static class WfpAppIdentity
{
    public static string GetDisplayPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return Path.GetFullPath(path.Trim().Trim('"'));
    }

    public static bool ContainsNonAscii(string path)
        => path.Any(static c => c > 127);

    public static IReadOnlyList<string> GetIdentityPaths(string inputPath, WfpAppIdentityPathMode mode)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            return [];
        }

        string trimmed = inputPath.Trim().Trim('"');
        switch (mode)
        {
            case WfpAppIdentityPathMode.ShortPathOnly:
                return [trimmed.Replace('/', '\\')];

            case WfpAppIdentityPathMode.LongPathOnly:
                return [Path.GetFullPath(trimmed)];

            default:
                return [Path.GetFullPath(trimmed)];
        }
    }

    public static bool PathsEquivalent(string a, string b)
        => string.Equals(
            Path.GetFullPath(a),
            Path.GetFullPath(b),
            StringComparison.OrdinalIgnoreCase);

    public static bool TryGetShortPath(string longPath, out string shortPath)
    {
        shortPath = string.Empty;
        if (string.IsNullOrWhiteSpace(longPath))
        {
            return false;
        }

        var sb = new System.Text.StringBuilder(512);
        uint len = Native.GetShortPathName(longPath, sb, (uint)sb.Capacity);
        if (len == 0 || len >= sb.Capacity)
        {
            sb.Capacity = (int)Math.Min(len + 1, 32768);
            len = Native.GetShortPathName(longPath, sb, (uint)sb.Capacity);
        }

        if (len == 0)
        {
            return false;
        }

        shortPath = sb.ToString(0, (int)len);
        return shortPath.Length > 0;
    }

    public static bool TryResolveAleAppIdFromFileName(string path, out string appId)
        => TryResolveAleAppIdFromFileName(path, out appId, out _);

    public static bool TryResolveAleAppIdFromFileName(string path, out string appId, out uint byteSize)
    {
        appId = string.Empty;
        byteSize = 0;
        if (!TryAcquireAleAppIdBlob(path, out IntPtr appIdPtr, out uint status))
        {
            return false;
        }

        try
        {
            if (!TryDecodeAleAppIdBlob(appIdPtr, out appId, out byteSize))
            {
                return false;
            }

            return appId.Length > 0;
        }
        finally
        {
            Native.FwpmFreeMemory0(ref appIdPtr);
        }
    }

    /// <summary>
    /// Returns FwpmGetAppIdFromFileName0 blob; caller must FwpmFreeMemory0 when done (e.g. after FwpmFilterAdd0).
    /// </summary>
    public static bool TryAcquireAleAppIdBlob(string path, out IntPtr appIdBlobPtr, out uint fwpmStatus)
    {
        appIdBlobPtr = IntPtr.Zero;
        fwpmStatus = 0;
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        fwpmStatus = Native.FwpmGetAppIdFromFileName0(path, out appIdBlobPtr);
        return fwpmStatus == 0 && appIdBlobPtr != IntPtr.Zero;
    }

    public static void FreeAleAppIdBlob(ref IntPtr appIdBlobPtr)
    {
        if (appIdBlobPtr != IntPtr.Zero)
        {
            Native.FwpmFreeMemory0(ref appIdBlobPtr);
        }
    }

    public static bool TryDecodeAleAppIdBlob(IntPtr appIdBlobPtr, out string appId, out uint byteSize)
    {
        appId = string.Empty;
        byteSize = 0;
        if (appIdBlobPtr == IntPtr.Zero)
        {
            return false;
        }

        FwpByteBlob blob = Marshal.PtrToStructure<FwpByteBlob>(appIdBlobPtr);
        byteSize = blob.Size;
        if (blob.Data == IntPtr.Zero || blob.Size == 0)
        {
            return false;
        }

        appId = Marshal.PtrToStringUni(blob.Data, (int)blob.Size / 2)?.TrimEnd('\0') ?? string.Empty;
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FwpByteBlob
    {
        public uint Size;
        public IntPtr Data;
    }

    private static class Native
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern uint GetShortPathName(string lpszLongPath, System.Text.StringBuilder lpszShortPath, uint cchBuffer);

        [DllImport("fwpuclnt.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Winapi)]
        public static extern uint FwpmGetAppIdFromFileName0(string fileName, out IntPtr appId);

        [DllImport("fwpuclnt.dll", CallingConvention = CallingConvention.Winapi)]
        public static extern void FwpmFreeMemory0(ref IntPtr p);
    }
}