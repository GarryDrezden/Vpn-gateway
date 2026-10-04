namespace SelectiveVpnRouter.Core;

public static class WfpActionDiagnostics
{
    public static string DescribeActionKind(uint actionType) =>
        actionType switch
        {
            WfpActionConstants.FwpActionPermit => "Permit",
            WfpActionConstants.FwpActionBlock => "Block",
            WfpActionConstants.FwpActionCalloutUnknown => "CalloutUnknown",
            _ when (actionType & WfpActionConstants.FwpActionFlagCallout) != 0 => "Callout",
            _ => "Other",
        };

    public static string FormatFilterAddContext(
        uint actionType,
        Guid layerKey,
        Guid subLayerKey,
        ulong weight,
        WfpFilterRole role) =>
        $"role={role} actionType=0x{actionType:X8} actionKind={DescribeActionKind(actionType)} "
        + $"layerKey={layerKey} subLayerKey={subLayerKey} weight={weight}";

    public static uint LoopbackPermitV4ActionType => WfpActionConstants.FwpActionPermit;
}