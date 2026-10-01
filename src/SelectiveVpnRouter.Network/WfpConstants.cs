using System.Net;

namespace SelectiveVpnRouter.Network;

/// <summary>
/// IPv4 values for FWPM_CONDITION_IP_REMOTE_ADDRESS (network byte order in uint32).
/// </summary>
public static class WfpIpv4AddressEncoding
{
    public static uint ToWfpIpv4AddressUInt32(string ipv4Address)
        => ToWfpIpv4AddressUInt32(IPAddress.Parse(ipv4Address));

    public static uint ToWfpIpv4AddressUInt32(IPAddress address)
    {
        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            throw new ArgumentException("IPv4 address required.", nameof(address));
        }

        byte[] b = address.GetAddressBytes();
        if (b.Length != 4)
        {
            throw new ArgumentException("IPv4 address must be 4 bytes.", nameof(address));
        }

        return ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
    }
}

public static class WfpConstants
{
    public static readonly Guid ConditionAleAppId = new("d78e1e87-8644-4ea5-9437-d809ecefc971");
    public static readonly Guid ConditionAleUserId = new("f06a2479-1b66-4c75-8663-786a4a33d382");
    public static readonly Guid LayerAleAuthConnectV6 = new("4a72393b-319f-44bc-84c3-ba54dcb3b6b4");

    /// <summary>Windows SDK fwpmk.h — FWPM_CONDITION_IP_PROTOCOL.</summary>
    public static readonly Guid ConditionIpProtocol = new("3971ef2b-623e-4f9a-8cb1-6e79b806b9a7");

    /// <summary>Windows SDK fwpmk.h — FWPM_CONDITION_IP_REMOTE_PORT.</summary>
    public static readonly Guid ConditionIpRemotePort = new("c35a604d-d22b-4e1a-91b4-68f674ee674b");

    /// <summary>Windows SDK fwpmk.h — FWPM_CONDITION_IP_REMOTE_ADDRESS.</summary>
    public static readonly Guid ConditionIpRemoteAddress = new("b235ae9a-1d64-49b8-a44c-5ff3d9095045");
    public const uint ExpectedByteBlobType = 12;
    public const uint ExpectedSecurityDescriptorType = 14;
    public const uint FwpEAlreadyExists = 0x80320009;
}

public sealed record WfpRuntimeCaptureStepProbeResult(
    string ConditionSet,
    uint FwpmFilterAddStatus,
    ulong FilterId,
    IReadOnlyList<string> ConditionLogLines);

public sealed record WfpGuidVerificationRow(string Name, Guid ManagedGuid, Guid SdkGuid, bool Equal);

public static class WfpRuntimeCaptureGuidVerification
{
    public static readonly Guid SdkConditionIpProtocol = new("3971ef2b-623e-4f9a-8cb1-6e79b806b9a7");
    public static readonly Guid SdkConditionIpRemotePort = new("c35a604d-d22b-4e1a-91b4-68f674ee674b");
    public static readonly Guid SdkConditionIpRemoteAddress = new("b235ae9a-1d64-49b8-a44c-5ff3d9095045");

    public static IReadOnlyList<WfpGuidVerificationRow> CompareManagedConditionGuidsToSdk() =>
    [
        Row("FWPM_CONDITION_IP_PROTOCOL", WfpConstants.ConditionIpProtocol, SdkConditionIpProtocol),
        Row("FWPM_CONDITION_IP_REMOTE_PORT", WfpConstants.ConditionIpRemotePort, SdkConditionIpRemotePort),
        Row("FWPM_CONDITION_IP_REMOTE_ADDRESS", WfpConstants.ConditionIpRemoteAddress, SdkConditionIpRemoteAddress),
    ];

    private static WfpGuidVerificationRow Row(string name, Guid managed, Guid sdk)
        => new(name, managed, sdk, managed == sdk);
}

public static class WfpRuntimeCaptureConditionDiagnostics
{
    public static string FormatGuidVerificationTable()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("| name | managed GUID | SDK GUID | equal |");
        sb.AppendLine("|------|--------------|----------|-------|");
        foreach (WfpGuidVerificationRow row in WfpRuntimeCaptureGuidVerification.CompareManagedConditionGuidsToSdk())
        {
            sb.AppendLine("| " + row.Name
                + " | " + row.ManagedGuid
                + " | " + row.SdkGuid
                + " | " + row.Equal
                + " |");
        }

        return sb.ToString();
    }

    public static string FormatStepProbeTable(IReadOnlyList<WfpRuntimeCaptureStepProbeResult> steps)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(WfpRuntimeCaptureConditionLayout.SummaryLine);
        sb.AppendLine();
        sb.AppendLine("| conditionSet | FwpmFilterAdd0Status | filterId | conditionLog |");
        sb.AppendLine("|--------------|----------------------|----------|--------------|");
        foreach (WfpRuntimeCaptureStepProbeResult step in steps)
        {
            string log = string.Join("; ", step.ConditionLogLines);
            sb.AppendLine("| " + step.ConditionSet
                + " | 0x" + step.FwpmFilterAddStatus.ToString("X8")
                + " | " + step.FilterId
                + " | " + log.Replace("|", "\\|", StringComparison.Ordinal)
                + " |");
        }

        return sb.ToString();
    }
}