using System.Runtime.InteropServices;
using System.Text;

namespace SelectiveVpnRouter.Network;

/// <summary>
/// Diagnostic-only ALE_APP_ID blob construction (not for production filter generation).
/// </summary>
public static class WfpDiagnosticAleAppIdBlob
{
    public static byte[] GetUtf16LeBytesIncludingTerminator(string canonicalAppId)
    {
        if (canonicalAppId is null)
        {
            throw new ArgumentNullException(nameof(canonicalAppId));
        }

        string text = canonicalAppId.TrimEnd('\0');
        return Encoding.Unicode.GetBytes(text + "\0");
    }

    public static uint ExpectedByteSizeForCanonicalString(string canonicalAppId)
        => (uint)GetUtf16LeBytesIncludingTerminator(canonicalAppId).Length;

    public static IntPtr BuildAleAppIdBlobFromCanonicalString(string canonicalAppId, out uint byteSize)
    {
        byte[] bytes = GetUtf16LeBytesIncludingTerminator(canonicalAppId);
        byteSize = (uint)bytes.Length;
        IntPtr dataPtr = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, dataPtr, bytes.Length);
        var blob = new DiagnosticFwpByteBlob
        {
            Size = byteSize,
            Data = dataPtr,
        };
        IntPtr blobPtr = Marshal.AllocHGlobal(Marshal.SizeOf<DiagnosticFwpByteBlob>());
        Marshal.StructureToPtr(blob, blobPtr, false);
        return blobPtr;
    }

    public static void FreeDiagnosticAleAppIdBlob(ref IntPtr blobPtr)
    {
        if (blobPtr == IntPtr.Zero)
        {
            return;
        }

        var blob = Marshal.PtrToStructure<DiagnosticFwpByteBlob>(blobPtr);
        if (blob.Data != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(blob.Data);
        }

        Marshal.FreeHGlobal(blobPtr);
        blobPtr = IntPtr.Zero;
    }

    public static bool TryCopyBlobBytes(IntPtr blobPtr, out byte[] bytes)
    {
        bytes = [];
        if (blobPtr == IntPtr.Zero)
        {
            return false;
        }

        DiagnosticFwpByteBlob blob = Marshal.PtrToStructure<DiagnosticFwpByteBlob>(blobPtr);
        if (blob.Data == IntPtr.Zero || blob.Size == 0)
        {
            return false;
        }

        bytes = new byte[blob.Size];
        Marshal.Copy(blob.Data, bytes, 0, (int)blob.Size);
        return true;
    }

    public static bool BytesEqual(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
        => a.SequenceEqual(b);

    public static string FormatFirstByteDifferences(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, int maxPairs = 4)
    {
        int max = Math.Max(a.Length, b.Length);
        var hits = new List<string>();
        for (int i = 0; i < max && hits.Count < maxPairs; i++)
        {
            byte av = i < a.Length ? a[i] : (byte)0;
            byte bv = i < b.Length ? b[i] : (byte)0;
            if (av != bv)
            {
                hits.Add("@" + i + ":0x" + av.ToString("X2") + " vs 0x" + bv.ToString("X2"));
            }
        }

        return hits.Count == 0 ? "(no byte differences)" : string.Join(" ", hits);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DiagnosticFwpByteBlob
    {
        public uint Size;
        public IntPtr Data;
    }
}