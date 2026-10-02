namespace SelectiveVpnRouter.Core;

public enum MultiAppRoutingSlot
{
    A,
    B,
    C,
}

public enum MultiAppExpectedRoute
{
    Vpn,
    Direct,
}

public enum MultiAppRealAppSpotVerdict
{
    Skipped,
    Pass,
    ObservedNoTraffic,
    Fail,
}

public sealed record MultiAppRoutingRowResult(
    MultiAppRoutingSlot Slot,
    MultiAppExpectedRoute Expected,
    MultiAppExpectedRoute Actual,
    bool Pass)
{
    public string SlotLabel => Slot switch
    {
        MultiAppRoutingSlot.A => "App A",
        MultiAppRoutingSlot.B => "App B",
        MultiAppRoutingSlot.C => "App C",
        _ => Slot.ToString(),
    };

    public string ExpectedLabel => Expected == MultiAppExpectedRoute.Vpn ? "VPN" : "DIRECT";
    public string ActualLabel => Actual == MultiAppExpectedRoute.Vpn ? "VPN" : "DIRECT";
    public string PassLabel => Pass ? "PASS" : "FAIL";

    public string FormatTableLine() =>
        $"{SlotLabel} | expected {ExpectedLabel} | actual {ActualLabel} | {PassLabel}";
}

public sealed record MultiAppRoutingPhaseDetail(
    string Phase,
    string App,
    MultiAppExpectedRoute Expected,
    MultiAppExpectedRoute Actual,
    string? PublicIp,
    bool ViaProxy,
    string? MatchedRuleName,
    int Pid,
    string? FlowProcessPath,
    bool Pass)
{
    public string FormatLine() =>
        $"{Phase} | {App} | expected {ExpectedRouteLabel(Expected)} | actual {ExpectedRouteLabel(Actual)} | ip={PublicIp ?? "-"} | viaProxy={ViaProxy} | rule={MatchedRuleName ?? "-"} | pid={Pid} | flowPath={FlowProcessPath ?? "-"} | {(Pass ? "PASS" : "FAIL")}";

    private static string ExpectedRouteLabel(MultiAppExpectedRoute route) =>
        route == MultiAppExpectedRoute.Vpn ? "VPN" : "DIRECT";
}

public sealed record MultiAppRoutingSummaryFlags(
    bool SimultaneousIsolation,
    bool CrossAttribution,
    bool DirectViaProxy,
    bool VpnAppsViaProxy,
    bool AbIsolation,
    bool AcIsolation,
    bool AbcIsolation,
    bool MultiFlowBurstOk,
    bool DiagnosticCleanupOk)
{
    public IEnumerable<string> FormatLines()
    {
        yield return "abIsolation=" + AbIsolation;
        yield return "acIsolation=" + AcIsolation;
        yield return "abcIsolation=" + AbcIsolation;
        yield return "multiFlowBurstOk=" + MultiFlowBurstOk;
        yield return "simultaneousIsolation=" + SimultaneousIsolation;
        yield return "crossAttribution=" + CrossAttribution;
        yield return "directViaProxy=" + DirectViaProxy;
        yield return "vpnAppsViaProxy=" + VpnAppsViaProxy;
        yield return "diagnosticCleanupOk=" + DiagnosticCleanupOk;
    }
}

public static class MultiAppRoutingIsolation
{
    public const string DiagnosticName = "multi-app-routing-isolation";
    public const string RegressionPassMarker = "MULTI-APP ROUTING REGRESSION: PASS";
    public const string RegressionFailMarker = "MULTI-APP ROUTING REGRESSION: FAIL";

    public const string TempRuleNameA = "tmp-multi-app-a";
    public const string TempRuleNameB = "tmp-multi-app-b";

    public static bool IsTempDiagnosticRuleName(string? ruleName) =>
        string.Equals(ruleName, TempRuleNameA, StringComparison.OrdinalIgnoreCase)
        || string.Equals(ruleName, TempRuleNameB, StringComparison.OrdinalIgnoreCase);

    public static MultiAppExpectedRoute ClassifyEgress(
        bool httpOk,
        bool wfpRedirect,
        bool proxyFlowObserved,
        string? localIp,
        string? directBaselineIp)
    {
        if (!httpOk)
        {
            return MultiAppExpectedRoute.Direct;
        }

        if (wfpRedirect || proxyFlowObserved)
        {
            return MultiAppExpectedRoute.Vpn;
        }

        if (localIp is not null && directBaselineIp is not null
            && string.Equals(localIp, directBaselineIp, StringComparison.OrdinalIgnoreCase))
        {
            return MultiAppExpectedRoute.Direct;
        }

        return MultiAppExpectedRoute.Direct;
    }

    /// <summary>
    /// Cross-attribution among probe apps only: ambiguous path match or A/B rule mismatch on a probe flow.
    /// Unrelated desktop flows are ignored (matches==0).
    /// </summary>
    public static bool HasCrossAttribution(
        IEnumerable<FlowEvent> flows,
        string pathA,
        string pathB,
        string pathC)
    {
        foreach (FlowEvent flow in flows)
        {
            if (string.IsNullOrWhiteSpace(flow.ProcessPath))
            {
                continue;
            }

            string path = flow.ProcessPath;
            bool isA = PathsEqual(path, pathA);
            bool isB = PathsEqual(path, pathB);
            bool isC = PathsEqual(path, pathC);
            int matches = (isA ? 1 : 0) + (isB ? 1 : 0) + (isC ? 1 : 0);
            if (matches == 0)
            {
                continue;
            }

            if (matches > 1)
            {
                return true;
            }

            if (isA && flow.WfpRedirect && IsForeignProbeRule(flow.RuleName, TempRuleNameA))
            {
                return true;
            }

            if (isB && flow.WfpRedirect && IsForeignProbeRule(flow.RuleName, TempRuleNameB))
            {
                return true;
            }
        }

        return false;
    }

    public static bool EvaluateSyntheticPass(
        IReadOnlyList<MultiAppRoutingRowResult> syntheticRows,
        MultiAppRoutingSummaryFlags flags)
    {
        if (syntheticRows.Any(r => !r.Pass))
        {
            return false;
        }

        return flags.AbIsolation
               && flags.AcIsolation
               && flags.AbcIsolation
               && flags.MultiFlowBurstOk
               && !flags.CrossAttribution
               && !flags.DirectViaProxy
               && flags.VpnAppsViaProxy
               && flags.DiagnosticCleanupOk;
    }

    public static bool ComputePhaseBasedVpnAppsViaProxy(
        IReadOnlyList<MultiAppRoutingPhaseDetail> phases,
        string pathA,
        string pathB)
    {
        bool sawVpnProbePhase = false;
        foreach (MultiAppRoutingPhaseDetail phase in phases)
        {
            if (phase.Expected != MultiAppExpectedRoute.Vpn)
            {
                continue;
            }

            if (!PhaseTargetsProbePath(phase, pathA) && !PhaseTargetsProbePath(phase, pathB))
            {
                continue;
            }

            sawVpnProbePhase = true;
            if (!phase.ViaProxy)
            {
                return false;
            }
        }

        return sawVpnProbePhase;
    }

    public static bool ComputePhaseBasedDirectViaProxy(
        IReadOnlyList<MultiAppRoutingPhaseDetail> phases,
        string pathC)
    {
        foreach (MultiAppRoutingPhaseDetail phase in phases)
        {
            if (phase.Expected != MultiAppExpectedRoute.Direct)
            {
                continue;
            }

            if (!PhaseTargetsProbePath(phase, pathC))
            {
                continue;
            }

            if (phase.ViaProxy)
            {
                return true;
            }
        }

        return false;
    }

    private static bool PhaseTargetsProbePath(MultiAppRoutingPhaseDetail phase, string probePath)
    {
        if (!string.IsNullOrWhiteSpace(phase.FlowProcessPath)
            && PathsEqual(phase.FlowProcessPath, probePath))
        {
            return true;
        }

        string fileName = Path.GetFileName(probePath);
        if (string.Equals(phase.App, fileName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return phase.App switch
        {
            "App A" => probePath.Contains("AppA", StringComparison.OrdinalIgnoreCase),
            "App B" => probePath.Contains("AppB", StringComparison.OrdinalIgnoreCase),
            "App C" => probePath.Contains("AppC", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    public static bool EvaluateOverallPass(
        IReadOnlyList<MultiAppRoutingRowResult> rows,
        MultiAppRoutingSummaryFlags flags) =>
        EvaluateSyntheticPass(rows, flags);

    public static string FormatAcceptanceReport(
        IReadOnlyList<MultiAppRoutingRowResult> rows,
        MultiAppRoutingSummaryFlags flags,
        int configuredPermanentVpnApps,
        int installedAppFilters,
        int observedProbeVpnPaths,
        int observedProbeDirectPaths,
        MultiAppRealAppSpotVerdict realAppVerdict,
        IReadOnlyList<string> extraLines)
    {
        var lines = new List<string> { "--- acceptance (synthetic A/B/C) ---" };
        lines.AddRange(rows.Select(r => r.FormatTableLine()));
        lines.Add($"configuredPermanentVpnApps={configuredPermanentVpnApps}");
        lines.Add($"installedAppFilters={installedAppFilters}");
        lines.Add($"postRunObservedProbeVpnPaths={observedProbeVpnPaths}");
        lines.Add($"postRunObservedProbeDirectPaths={observedProbeDirectPaths}");
        lines.AddRange(flags.FormatLines());
        lines.Add("realAppSpotCheck=" + RealAppVerdictLabel(realAppVerdict));
        lines.AddRange(extraLines);
        lines.Add(EvaluateSyntheticPass(rows, flags) ? RegressionPassMarker : RegressionFailMarker);
        return string.Join(Environment.NewLine, lines);
    }

    public static string FormatPhaseReport(IReadOnlyList<MultiAppRoutingPhaseDetail> phases)
    {
        if (phases.Count == 0)
        {
            return "--- phases ---\n(none)";
        }

        var lines = new List<string> { "--- phases ---" };
        lines.Add("phase | app | expected | actual | publicIp | viaProxy | matchedFilterId | PID | flow attribution/app path | PASS/FAIL");
        lines.AddRange(phases.Select(p => p.FormatLine()));
        return string.Join(Environment.NewLine, lines);
    }

    public static bool MessageIndicatesPass(string? message) =>
        !string.IsNullOrWhiteSpace(message)
        && message.Contains(RegressionPassMarker, StringComparison.Ordinal);

    public static int CountDistinctProbePaths(
        IEnumerable<FlowEvent> flows,
        string pathA,
        string pathB,
        string pathC,
        MultiAppExpectedRoute routeKind)
    {
        HashSet<string> set = new(StringComparer.OrdinalIgnoreCase);
        foreach (FlowEvent flow in flows)
        {
            if (string.IsNullOrWhiteSpace(flow.ProcessPath))
            {
                continue;
            }

            bool isProbe = PathsEqual(flow.ProcessPath, pathA)
                || PathsEqual(flow.ProcessPath, pathB)
                || PathsEqual(flow.ProcessPath, pathC);
            if (!isProbe)
            {
                continue;
            }

            bool isVpn = flow.WfpRedirect || flow.Route == FlowRoute.Vpn;
            bool isDirect = !flow.WfpRedirect && flow.Route == FlowRoute.Direct;
            if (routeKind == MultiAppExpectedRoute.Vpn && isVpn)
            {
                set.Add(flow.ProcessPath);
            }
            else if (routeKind == MultiAppExpectedRoute.Direct && isDirect)
            {
                set.Add(flow.ProcessPath);
            }
        }

        return set.Count;
    }

    private static bool IsForeignProbeRule(string? ruleName, string expectedRuleName)
    {
        if (string.IsNullOrWhiteSpace(ruleName))
        {
            return false;
        }

        if (string.Equals(ruleName, expectedRuleName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return IsTempDiagnosticRuleName(ruleName);
    }

    private static string RealAppVerdictLabel(MultiAppRealAppSpotVerdict verdict) =>
        verdict switch
        {
            MultiAppRealAppSpotVerdict.Pass => "PASS",
            MultiAppRealAppSpotVerdict.ObservedNoTraffic => "OBSERVED-NO-TRAFFIC",
            MultiAppRealAppSpotVerdict.Fail => "FAIL",
            _ => "SKIPPED",
        };

    private static bool PathsEqual(string a, string b) =>
        ApplicationRulesHelper.PathsEqual(a, b);
}
