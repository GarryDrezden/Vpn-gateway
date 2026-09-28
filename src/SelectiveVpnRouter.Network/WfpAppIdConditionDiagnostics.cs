using System.Runtime.InteropServices;
using System.Text;
using static SelectiveVpnRouter.Network.WfpNativeTypes;

namespace SelectiveVpnRouter.Network;

public sealed record WfpAppIdConditionValidation(bool IsValid, string DiagnosticLine, string? Error);

public static class WfpAppIdConditionDiagnostics
{
    public static WfpAppIdConditionValidation ValidateAppIdCondition(
        Guid fieldKey,
        uint matchType,
        uint valueType,
        IntPtr appIdBlobPtr)
    {
        uint blobSize = 0;
        IntPtr blobData = IntPtr.Zero;
        if (appIdBlobPtr != IntPtr.Zero)
        {
            FWP_BYTE_BLOB blob = Marshal.PtrToStructure<FWP_BYTE_BLOB>(appIdBlobPtr);
            blobSize = blob.size;
            blobData = blob.data;
        }

        var diagnostic = new StringBuilder();
        diagnostic.Append($"fieldKey={fieldKey} ");
        diagnostic.Append($"expectedField={WfpConstants.ConditionAleAppId} ");
        diagnostic.Append($"matchType={matchType} ");
        diagnostic.Append($"valueType={valueType} ");
        diagnostic.Append($"expectedValueType={WfpConstants.ExpectedByteBlobType} ");
        diagnostic.Append($"appIdPtr=0x{appIdBlobPtr.ToInt64():X} ");
        diagnostic.Append($"appIdBlobSize={blobSize} ");
        diagnostic.Append($"appIdBlobData=0x{blobData.ToInt64():X}");

        if (fieldKey != WfpConstants.ConditionAleAppId)
        {
            return Fail(diagnostic.ToString(), "fieldKey is not FWPM_CONDITION_ALE_APP_ID.");
        }

        if (valueType != WfpConstants.ExpectedByteBlobType)
        {
            return Fail(diagnostic.ToString(),
                $"valueType={valueType} is not FWP_BYTE_BLOB_TYPE ({WfpConstants.ExpectedByteBlobType}).");
        }

        if (appIdBlobPtr == IntPtr.Zero || blobSize == 0 || blobData == IntPtr.Zero)
        {
            return Fail(diagnostic.ToString(), "appId FWP_BYTE_BLOB is empty or not allocated.");
        }

        return new WfpAppIdConditionValidation(true, diagnostic.ToString(), null);
    }

    private static WfpAppIdConditionValidation Fail(string diagnostic, string error) =>
        new(false, diagnostic, error);
}