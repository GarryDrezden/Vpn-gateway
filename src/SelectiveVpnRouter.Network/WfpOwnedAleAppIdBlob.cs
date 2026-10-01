using System.Runtime.InteropServices;

namespace SelectiveVpnRouter.Network;

/// <summary>
/// Native FWP_BYTE_BLOB for ALE_APP_ID owned by managed code (Marshal.AllocHGlobal).
/// Must remain alive through FwpmFilterAdd0; safe to dispose after filter add completes.
/// </summary>
public sealed class WfpOwnedAleAppIdBlob : IDisposable
{
    private bool _disposed;
    private IntPtr _blobPointer;

    internal WfpOwnedAleAppIdBlob(
        IntPtr blobPointer,
        uint byteSize,
        string fwpmCanonicalAppId,
        string normalizedAppId)
    {
        _blobPointer = blobPointer;
        ByteSize = byteSize;
        FwpmCanonicalAppId = fwpmCanonicalAppId;
        NormalizedAppId = normalizedAppId;
    }

    public IntPtr BlobPointer => _blobPointer;

    public uint ByteSize { get; }

    public string FwpmCanonicalAppId { get; }

    public string NormalizedAppId { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        WfpAleAppIdBlobMemory.FreeOwnedBlob(ref _blobPointer);
    }
}

internal static class WfpAleAppIdBlobMemory
{
    [StructLayout(LayoutKind.Sequential)]
    private struct FwpByteBlob
    {
        public uint Size;
        public IntPtr Data;
    }

    public static IntPtr AllocateBlobFromUtf16LeBytes(byte[] bytes, out uint byteSize)
    {
        byteSize = (uint)bytes.Length;
        IntPtr dataPtr = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, dataPtr, bytes.Length);
        var blob = new FwpByteBlob
        {
            Size = byteSize,
            Data = dataPtr,
        };
        IntPtr blobPtr = Marshal.AllocHGlobal(Marshal.SizeOf<FwpByteBlob>());
        Marshal.StructureToPtr(blob, blobPtr, false);
        return blobPtr;
    }

    public static void FreeOwnedBlob(ref IntPtr blobPtr)
    {
        if (blobPtr == IntPtr.Zero)
        {
            return;
        }

        FwpByteBlob blob = Marshal.PtrToStructure<FwpByteBlob>(blobPtr);
        if (blob.Data != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(blob.Data);
        }

        Marshal.FreeHGlobal(blobPtr);
        blobPtr = IntPtr.Zero;
    }
}