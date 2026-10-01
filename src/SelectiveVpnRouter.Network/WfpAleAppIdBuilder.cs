using System.Text;

namespace SelectiveVpnRouter.Network;

/// <summary>
/// Production ALE_APP_ID blob builder: Fwpm canonical string -> invariant lowercase -> UTF-16LE + NUL.
/// </summary>
public static class WfpAleAppIdBuilder
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

    public static bool TryBuildNormalizedForFilePath(
        string filePath,
        out WfpOwnedAleAppIdBlob? ownedBlob,
        out string? error)
    {
        ownedBlob = null;
        error = null;
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(filePath))
        {
            error = "Invalid file path.";
            return false;
        }

        if (!WfpAppIdentity.TryAcquireAleAppIdBlob(filePath, out IntPtr fwpmBlobPtr, out uint fwpmStatus))
        {
            error = "FwpmGetAppIdFromFileName0 failed: 0x" + fwpmStatus.ToString("X8");
            return false;
        }

        try
        {
            if (!WfpAppIdentity.TryDecodeAleAppIdBlob(fwpmBlobPtr, out string fwpmCanonical, out _))
            {
                error = "Failed to decode Fwpm APP_ID blob.";
                return false;
            }

            if (fwpmCanonical.Length == 0)
            {
                error = "Fwpm APP_ID string is empty.";
                return false;
            }

            return TryBuildNormalizedFromFwpmCanonical(fwpmCanonical, out ownedBlob, out error);
        }
        finally
        {
            WfpAppIdentity.FreeAleAppIdBlob(ref fwpmBlobPtr);
        }
    }

    public static bool TryBuildNormalizedFromFwpmCanonical(
        string fwpmCanonicalAppId,
        out WfpOwnedAleAppIdBlob? ownedBlob,
        out string? error)
    {
        ownedBlob = null;
        error = null;
        if (string.IsNullOrEmpty(fwpmCanonicalAppId))
        {
            error = "Fwpm canonical APP_ID is empty.";
            return false;
        }

        string normalized = fwpmCanonicalAppId.ToLowerInvariant();
        byte[] bytes = GetUtf16LeBytesIncludingTerminator(normalized);
        IntPtr blobPtr = WfpAleAppIdBlobMemory.AllocateBlobFromUtf16LeBytes(bytes, out uint byteSize);
        ownedBlob = new WfpOwnedAleAppIdBlob(blobPtr, byteSize, fwpmCanonicalAppId, normalized);
        return true;
    }
}