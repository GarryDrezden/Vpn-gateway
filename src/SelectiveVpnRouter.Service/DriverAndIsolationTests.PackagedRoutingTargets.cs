using SelectiveVpnRouter.Core;

namespace SelectiveVpnRouter.Service;

internal static partial class DriverAndIsolationTests
{
    private static DiagnosticResult PackagedRoutingTargets(RouterEngine engine)
    {
        var resolver = new WindowsPackagedApplicationPathResolver();
        string report = PackagedRoutingTargetsDiagnostic.FormatAllPackagedVpnRules(
            engine.Config,
            resolver,
            engine.WfpPolicy);

        bool anyMissing = PackagedRoutingTargetResolver.ResolveAllVpnApplicationRules(engine.Config.Rules, resolver)
            .Where(p => p.Rule.Mode == RouteMode.Vpn)
            .SelectMany(p => p.Targets)
            .Any(t => t.Resolved
                && t.Kind == PackagedRoutingTargetKind.AssociatedHelper
                && !WfpPolicyHealth.IsVpnAppWfpReady(engine.WfpPolicy, t.ExecutablePath));

        if (anyMissing)
        {
            return Warning(
                "packaged-routing-targets",
                report + Environment.NewLine + Environment.NewLine
                    + "WARNING: At least one associated helper executable is missing VPN WFP filters. "
                    + "OAuth/token HTTP from that process may egress DIRECT.");
        }

        return Pass("packaged-routing-targets", report);
    }
}
