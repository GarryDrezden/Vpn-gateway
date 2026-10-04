namespace SelectiveVpnRouter.Core;

public static class PackagedRoutingTargetsDiagnostic
{
    public static string FormatRuleReport(
        PackagedRoutingTargetsForRule plan,
        WfpPolicyDiagnostics? wfpPolicy)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(plan.Rule.Name + " -> " + plan.Rule.Mode);
        sb.AppendLine("PFN: " + (plan.Rule.PackagedBinding?.PackageFamilyName ?? "(win32)"));
        sb.AppendLine("ApplicationId: " + (plan.Rule.PackagedBinding?.ApplicationId ?? "(n/a)"));

        foreach (PackagedRoutingTarget target in plan.Targets)
        {
            string role = target.Kind == PackagedRoutingTargetKind.Primary ? "Primary" : "Associated";
            sb.AppendLine(role + ":");
            sb.Append("  appId: ").AppendLine(target.ApplicationId);
            if (!target.Resolved)
            {
                sb.Append("  path: (unresolved) ").AppendLine(target.UnresolvedReason ?? "unknown");
                sb.AppendLine("  WFP: MISSING (path unresolved)");
                continue;
            }

            sb.Append("  path: ").AppendLine(target.ExecutablePath);
            sb.AppendLine("  WFP: " + DescribeWfp(plan.Rule.Mode, target.ExecutablePath, wfpPolicy));
        }

        return sb.ToString().TrimEnd();
    }

    public static string FormatAllPackagedVpnRules(
        AppConfiguration config,
        IPackagedApplicationPathResolver? resolver,
        WfpPolicyDiagnostics? wfpPolicy)
    {
        var plans = PackagedRoutingTargetResolver.ResolveAllVpnApplicationRules(config.Rules, resolver)
            .Where(p => p.Rule.PackagedBinding is not null)
            .ToList();

        if (plans.Count == 0)
        {
            return "No packaged VPN application rules configured.";
        }

        return string.Join(
            Environment.NewLine + Environment.NewLine,
            plans.Select(p => FormatRuleReport(p, wfpPolicy)));
    }

    private static string DescribeWfp(RouteMode mode, string executablePath, WfpPolicyDiagnostics? policy)
    {
        if (mode != RouteMode.Vpn)
        {
            return "DIRECT (rule mode Direct — no VPN redirect filters)";
        }

        if (policy is null || policy.Filters.Count == 0)
        {
            return "UNKNOWN (WFP policy snapshot unavailable)";
        }

        bool redirect = WfpPolicyHealth.IsExeFilterReady(WfpPolicyHealth.FindCalloutFilter(policy, executablePath));
        bool loopback = WfpPolicyHealth.IsLoopbackPermitReady(WfpPolicyHealth.FindLoopbackPermitFilter(policy, executablePath));
        if (redirect && loopback)
        {
            return "VPN (redirect + loopback PERMIT installed)";
        }

        if (!redirect && !loopback)
        {
            return "MISSING (no redirect/loopback filters for this executable)";
        }

        return "PARTIAL redirect=" + redirect + " loopbackPermit=" + loopback;
    }
}
