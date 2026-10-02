using SelectiveVpnRouter.Core;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class MultiAppRoutingIsolationTests
{
    [Fact]
    public void ClassifyEgress_vpn_when_wfp_redirect()
    {
        MultiAppExpectedRoute route = MultiAppRoutingIsolation.ClassifyEgress(
            httpOk: true,
            wfpRedirect: true,
            proxyFlowObserved: false,
            localIp: "1.1.1.1",
            directBaselineIp: "1.1.1.1");
        Assert.Equal(MultiAppExpectedRoute.Vpn, route);
    }

    [Fact]
    public void ClassifyEgress_direct_when_matches_baseline()
    {
        MultiAppExpectedRoute route = MultiAppRoutingIsolation.ClassifyEgress(
            httpOk: true,
            wfpRedirect: false,
            proxyFlowObserved: false,
            localIp: "82.0.0.1",
            directBaselineIp: "82.0.0.1");
        Assert.Equal(MultiAppExpectedRoute.Direct, route);
    }

    [Fact]
    public void HasCrossAttribution_false_for_distinct_paths()
    {
        var flows = new[]
        {
            new FlowEvent { ProcessPath = @"C:\A\a.exe", WfpRedirect = true, RuleName = MultiAppRoutingIsolation.TempRuleNameA },
            new FlowEvent { ProcessPath = @"C:\B\b.exe", WfpRedirect = true, RuleName = MultiAppRoutingIsolation.TempRuleNameB },
        };
        Assert.False(MultiAppRoutingIsolation.HasCrossAttribution(flows, flows[0].ProcessPath, flows[1].ProcessPath, @"C:\C\c.exe"));
    }

    [Fact]
    public void HasCrossAttribution_ignores_unrelated_zero_match_flows()
    {
        var flows = new[]
        {
            new FlowEvent { ProcessPath = @"C:\Telegram\Telegram.exe", WfpRedirect = false },
            new FlowEvent { ProcessPath = @"C:\A\a.exe", WfpRedirect = true, RuleName = MultiAppRoutingIsolation.TempRuleNameA },
        };
        Assert.False(MultiAppRoutingIsolation.HasCrossAttribution(flows, @"C:\A\a.exe", @"C:\B\b.exe", @"C:\C\c.exe"));
    }

    [Fact]
    public void HasCrossAttribution_true_for_ambiguous_probe_path()
    {
        var flows = new[]
        {
            new FlowEvent { ProcessPath = @"C:\weird.exe", WfpRedirect = true, RuleName = MultiAppRoutingIsolation.TempRuleNameA },
        };
        Assert.True(MultiAppRoutingIsolation.HasCrossAttribution(
            flows,
            @"C:\weird.exe",
            @"C:\weird.exe",
            @"C:\C\c.exe"));
    }

    [Fact]
    public void ComputePhaseBasedVpnAppsViaProxy_uses_phase_not_post_run_snapshot()
    {
        string pathA = @"C:\ProgramData\VPN Route Tests\AppA\SelectiveVpnRouter.Probe.exe";
        string pathB = @"C:\ProgramData\VPN Route Tests\AppB\SelectiveVpnRouter.Probe.exe";
        var phases = new[]
        {
            new MultiAppRoutingPhaseDetail("A alone", "App A", MultiAppExpectedRoute.Vpn, MultiAppExpectedRoute.Vpn, null, true, null, 1, pathA, true),
            new MultiAppRoutingPhaseDetail("B alone", "App B", MultiAppExpectedRoute.Vpn, MultiAppExpectedRoute.Vpn, null, true, null, 2, pathB, true),
        };
        Assert.True(MultiAppRoutingIsolation.ComputePhaseBasedVpnAppsViaProxy(phases, pathA, pathB));
    }

    [Fact]
    public void Synthetic_pass_with_empty_post_run_counters_when_phases_ok()
    {
        var rows = new[]
        {
            new MultiAppRoutingRowResult(MultiAppRoutingSlot.A, MultiAppExpectedRoute.Vpn, MultiAppExpectedRoute.Vpn, true),
            new MultiAppRoutingRowResult(MultiAppRoutingSlot.B, MultiAppExpectedRoute.Vpn, MultiAppExpectedRoute.Vpn, true),
            new MultiAppRoutingRowResult(MultiAppRoutingSlot.C, MultiAppExpectedRoute.Direct, MultiAppExpectedRoute.Direct, true),
        };
        string pathA = @"C:\ProgramData\VPN Route Tests\AppA\SelectiveVpnRouter.Probe.exe";
        string pathB = @"C:\ProgramData\VPN Route Tests\AppB\SelectiveVpnRouter.Probe.exe";
        var phases = new[]
        {
            new MultiAppRoutingPhaseDetail("A alone", "App A", MultiAppExpectedRoute.Vpn, MultiAppExpectedRoute.Vpn, null, true, null, 1, pathA, true),
            new MultiAppRoutingPhaseDetail("B alone", "App B", MultiAppExpectedRoute.Vpn, MultiAppExpectedRoute.Vpn, null, true, null, 2, pathB, true),
        };
        bool vpnViaProxy = MultiAppRoutingIsolation.ComputePhaseBasedVpnAppsViaProxy(phases, pathA, pathB);
        var flags = new MultiAppRoutingSummaryFlags(true, false, false, vpnViaProxy, true, true, true, true, true);
        string report = MultiAppRoutingIsolation.FormatAcceptanceReport(rows, flags, 1, 4, 0, 0, MultiAppRealAppSpotVerdict.ObservedNoTraffic, Array.Empty<string>());
        Assert.Contains("postRunObservedProbeVpnPaths=0", report);
        Assert.Contains(MultiAppRoutingIsolation.RegressionPassMarker, report);
    }
    [Fact]
    public void FormatAcceptanceReport_contains_pass_marker()
    {
        var rows = new[]
        {
            new MultiAppRoutingRowResult(MultiAppRoutingSlot.A, MultiAppExpectedRoute.Vpn, MultiAppExpectedRoute.Vpn, true),
            new MultiAppRoutingRowResult(MultiAppRoutingSlot.B, MultiAppExpectedRoute.Vpn, MultiAppExpectedRoute.Vpn, true),
            new MultiAppRoutingRowResult(MultiAppRoutingSlot.C, MultiAppExpectedRoute.Direct, MultiAppExpectedRoute.Direct, true),
        };
        var flags = new MultiAppRoutingSummaryFlags(true, false, false, true, true, true, true, true, true);
        string report = MultiAppRoutingIsolation.FormatAcceptanceReport(rows, flags, 1, 3, 2, 1, MultiAppRealAppSpotVerdict.ObservedNoTraffic, Array.Empty<string>());
        Assert.Contains(MultiAppRoutingIsolation.RegressionPassMarker, report);
        Assert.Contains("abIsolation=True", report);
        Assert.Contains("realAppSpotCheck=OBSERVED-NO-TRAFFIC", report);
    }

    [Fact]
    public void EvaluateSyntheticPass_fails_when_direct_via_proxy()
    {
        var rows = new[]
        {
            new MultiAppRoutingRowResult(MultiAppRoutingSlot.C, MultiAppExpectedRoute.Direct, MultiAppExpectedRoute.Direct, true),
        };
        var flags = new MultiAppRoutingSummaryFlags(true, false, true, true, true, true, true, true, true);
        Assert.False(MultiAppRoutingIsolation.EvaluateSyntheticPass(rows, flags));
    }

    [Fact]
    public void EvaluateSyntheticPass_true_for_abc_simultaneous_matrix()
    {
        var rows = new[]
        {
            new MultiAppRoutingRowResult(MultiAppRoutingSlot.C, MultiAppExpectedRoute.Direct, MultiAppExpectedRoute.Direct, true),
            new MultiAppRoutingRowResult(MultiAppRoutingSlot.A, MultiAppExpectedRoute.Vpn, MultiAppExpectedRoute.Vpn, true),
            new MultiAppRoutingRowResult(MultiAppRoutingSlot.B, MultiAppExpectedRoute.Vpn, MultiAppExpectedRoute.Vpn, true),
        };
        var flags = new MultiAppRoutingSummaryFlags(true, false, false, true, true, true, true, true, true);
        Assert.True(MultiAppRoutingIsolation.EvaluateSyntheticPass(rows, flags));
    }

    [Fact]
    public void IsTempDiagnosticRuleName_excludes_tmp_multi_app_from_real_apps()
    {
        Assert.True(MultiAppRoutingIsolation.IsTempDiagnosticRuleName(MultiAppRoutingIsolation.TempRuleNameA));
        Assert.True(MultiAppRoutingIsolation.IsTempDiagnosticRuleName(MultiAppRoutingIsolation.TempRuleNameB));
    }

    [Fact]
    public void Real_app_observed_no_traffic_does_not_fail_synthetic()
    {
        var rows = new[]
        {
            new MultiAppRoutingRowResult(MultiAppRoutingSlot.A, MultiAppExpectedRoute.Vpn, MultiAppExpectedRoute.Vpn, true),
            new MultiAppRoutingRowResult(MultiAppRoutingSlot.B, MultiAppExpectedRoute.Vpn, MultiAppExpectedRoute.Vpn, true),
            new MultiAppRoutingRowResult(MultiAppRoutingSlot.C, MultiAppExpectedRoute.Direct, MultiAppExpectedRoute.Direct, true),
        };
        var flags = new MultiAppRoutingSummaryFlags(true, false, false, true, true, true, true, true, true);
        string report = MultiAppRoutingIsolation.FormatAcceptanceReport(rows, flags, 1, 4, 2, 1, MultiAppRealAppSpotVerdict.ObservedNoTraffic, new[] { "realApps=Telegram Desktop=OBSERVED-NO-TRAFFIC" });
        Assert.Contains(MultiAppRoutingIsolation.RegressionPassMarker, report);
    }
}
