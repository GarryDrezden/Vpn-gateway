namespace SelectiveVpnRouter.Core;

public static class TransparentRoutingCriteria
{
    public sealed record Input(
        bool CalloutMatched,
        bool ApplyModified,
        bool ProxyAccepted,
        bool RedirectContextRecovered,
        bool ProxyFlowObserved,
        bool VpnOk,
        bool DirectOk,
        bool DirectViaProxy,
        bool LocalsDiffer,
        bool VpnConnected);

    public static bool IsPass(Input input) =>
        input.CalloutMatched
        && input.ApplyModified
        && input.ProxyAccepted
        && input.RedirectContextRecovered
        && input.ProxyFlowObserved
        && input.VpnOk
        && input.DirectOk
        && !input.DirectViaProxy
        && input.LocalsDiffer
        && input.VpnConnected;
}