using SelectiveVpnRouter.Core;
using MultiAppRoutingCore = SelectiveVpnRouter.Core.MultiAppRoutingIsolation;
using SelectiveVpnRouter.Network;
namespace SelectiveVpnRouter.Service;
internal static partial class DriverAndIsolationTests
{
    private static async Task<DiagnosticResult> RunMultiAppRoutingIsolationAsync(RouterEngine engine, CancellationToken ct)
    {
        string name = MultiAppRoutingCore.DiagnosticName;
        if (FindProbe() is null)
        {
            return Fail(name, Bilingual("Probe.exe missing.", "Probe.exe ne naiden."));
        }
        ServiceSnapshot pre = engine.Snapshot();
        if (!pre.DriverLoaded)
        {
            return Fail(name, Bilingual(
                "MULTI-APP ROUTING REGRESSION: FAIL — callout driver not loaded.",
                "FAIL — callout-draiver ne zagruzhen."));
        }
        if (engine.ProxyPort is null)
        {
            return Fail(name, Bilingual(
                "MULTI-APP ROUTING REGRESSION: FAIL — proxy not running. Connect VPN Route first.",
                "FAIL — proksi ne zapushchen."));
        }
        string pathA;
        string pathB;
        string pathC;
        try
        {
            pathA = Path.GetFullPath(ProbeCopyHelper.PrepareProbeCopy(MultiAppRoutingProbePaths.SlotDirectory(MultiAppRoutingProbePaths.SlotA)));
            pathB = Path.GetFullPath(ProbeCopyHelper.PrepareProbeCopy(MultiAppRoutingProbePaths.SlotDirectory(MultiAppRoutingProbePaths.SlotB)));
            pathC = Path.GetFullPath(ProbeCopyHelper.PrepareProbeCopy(MultiAppRoutingProbePaths.SlotDirectory(MultiAppRoutingProbePaths.SlotC)));
        }
        catch (Exception ex)
        {
            return Fail(name, ProbeInfrastructureFailure(ex.Message));
        }
        AppConfiguration previous = engine.Config;
        var rows = new List<MultiAppRoutingRowResult>();
        var phases = new List<MultiAppRoutingPhaseDetail>();
        var log = new List<string>();
        IReadOnlyList<RoutingRule> permanentVpnBeforeDiagnostic = ApplicationRulesHelper
            .GetPermanentApplicationRules(previous)
            .Where(r => r.Enabled && r.Mode == RouteMode.Vpn)
            .ToList();
        await RemoveStaleDiagnosticRulesAsync(engine, log, ct).ConfigureAwait(false);
        try
        {
            List<RoutingRule> tempRules =
            [
                RoutingRule.Create(RuleType.Application, MultiAppRoutingCore.TempRuleNameA, pathA, RouteMode.Vpn),
                RoutingRule.Create(RuleType.Application, MultiAppRoutingCore.TempRuleNameB, pathB, RouteMode.Vpn),
            ];
            engine.SaveConfig(previous with { Rules = StripMultiAppTempRules(previous.Rules).Concat(tempRules).ToList() });
            await engine.RefreshPolicyAsync().ConfigureAwait(false);
            WfpPolicyDiagnostics wfp = engine.WfpPolicy;
            if (!WfpPolicyHealth.IsExeFilterReady(WfpPolicyHealth.FindCalloutFilter(wfp, pathA))
                || !WfpPolicyHealth.IsExeFilterReady(WfpPolicyHealth.FindCalloutFilter(wfp, pathB)))
            {
                return Fail(name, WfpPolicyFailure(pathA, WfpPolicyHealth.FindCalloutFilter(wfp, pathA), wfp));
            }
            string url = engine.Config.Vpn.PublicIpEndpoint ?? "https://api.ipify.org";
            MultiAppRoutingRowResult rowC = await RunMultiAppProbeAsync(engine, pathC, MultiAppRoutingSlot.C, MultiAppExpectedRoute.Direct, url, null, ct).ConfigureAwait(false);
            rows.Add(rowC);
            RecordRowAsPhase(phases, engine, "C alone", pathC, rowC, null);
            string? directBaseline = await ReadLocalIpAsync(pathC, url, ct).ConfigureAwait(false);
            MultiAppRoutingRowResult rowAAlone = await RunMultiAppProbeAsync(engine, pathA, MultiAppRoutingSlot.A, MultiAppExpectedRoute.Vpn, url, directBaseline, ct).ConfigureAwait(false);
            rows.Add(rowAAlone);
            RecordRowAsPhase(phases, engine, "A alone", pathA, rowAAlone, directBaseline);
            MultiAppRoutingRowResult rowBAlone = await RunMultiAppProbeAsync(engine, pathB, MultiAppRoutingSlot.B, MultiAppExpectedRoute.Vpn, url, directBaseline, ct).ConfigureAwait(false);
            rows.Add(rowBAlone);
            RecordRowAsPhase(phases, engine, "B alone", pathB, rowBAlone, directBaseline);
            IReadOnlyList<MultiAppRoutingRowResult> simAbRows = await RunMultiAppSimultaneousAsync(engine, url, directBaseline, pathA, pathB, pathC,
                [MultiAppRoutingSlot.A, MultiAppRoutingSlot.B], ct).ConfigureAwait(false);
            bool simAb = simAbRows.All(r => r.Pass);
            RecordSimultaneousPhases(phases, engine, "AB simultaneous", simAbRows, pathA, pathB, pathC, directBaseline);
            IReadOnlyList<MultiAppRoutingRowResult> simAcRows = await RunMultiAppSimultaneousAsync(engine, url, directBaseline, pathA, pathB, pathC,
                [MultiAppRoutingSlot.A, MultiAppRoutingSlot.C], ct).ConfigureAwait(false);
            bool simAc = simAcRows.All(r => r.Pass);
            RecordSimultaneousPhases(phases, engine, "AC simultaneous", simAcRows, pathA, pathB, pathC, directBaseline);
            IReadOnlyList<MultiAppRoutingRowResult> simAbcRows = await RunMultiAppSimultaneousAsync(engine, url, directBaseline, pathA, pathB, pathC,
                [MultiAppRoutingSlot.A, MultiAppRoutingSlot.B, MultiAppRoutingSlot.C], ct).ConfigureAwait(false);
            bool simAbc = simAbcRows.All(r => r.Pass);
            RecordSimultaneousPhases(phases, engine, "ABC simultaneous", simAbcRows, pathA, pathB, pathC, directBaseline);
            bool multiFlowOk = await RunMultiFlowBurstAsync(engine, pathA, url, "A burst", phases, ct).ConfigureAwait(false)
                               && await RunMultiFlowBurstAsync(engine, pathB, url, "B burst", phases, ct).ConfigureAwait(false);
            await RunRuleIsolationAsync(engine, previous, pathA, pathB, url, directBaseline, rows, ct).ConfigureAwait(false);
            await RunAppRestartIsolationAsync(engine, pathA, pathB, url, ct).ConfigureAwait(false);
            ServiceSnapshot snap = engine.Snapshot();
            IEnumerable<FlowEvent> userFlows = FlowPresentationHelper.SelectUserFlows(snap.Flows, 500);
            IEnumerable<FlowEvent> probeFlows = userFlows.Where(f =>
                ApplicationRulesHelper.PathsEqual(f.ProcessPath, pathA)
                || ApplicationRulesHelper.PathsEqual(f.ProcessPath, pathB)
                || ApplicationRulesHelper.PathsEqual(f.ProcessPath, pathC));
            bool cross = MultiAppRoutingCore.HasCrossAttribution(probeFlows, pathA, pathB, pathC);
            bool directViaProxy = MultiAppRoutingCore.ComputePhaseBasedDirectViaProxy(phases, pathC);
            bool vpnViaProxy = MultiAppRoutingCore.ComputePhaseBasedVpnAppsViaProxy(phases, pathA, pathB);
            int configuredPermanentVpn = permanentVpnBeforeDiagnostic.Count;
            int installedFilters = snap.WfpPolicy.InstalledAppFilters;
            int observedProbeVpn = MultiAppRoutingCore.CountDistinctProbePaths(probeFlows, pathA, pathB, pathC, MultiAppExpectedRoute.Vpn);
            int observedProbeDirect = MultiAppRoutingCore.CountDistinctProbePaths(probeFlows, pathA, pathB, pathC, MultiAppExpectedRoute.Direct);
            (MultiAppRealAppSpotVerdict realVerdict, List<string> realNotes) = await RunRealAppSpotCheckAsync(
                engine, permanentVpnBeforeDiagnostic, url, directBaseline, ct).ConfigureAwait(false);
            foreach (string note in realNotes)
            {
                log.Add(note);
            }
            var extraLines = BuildInstalledFilterBreakdown(engine, snap);
            _pendingSyntheticRows = rows.Take(3).ToList();
            _pendingSimultaneous = simAb && simAc && simAbc && multiFlowOk;
            _pendingCross = cross;
            _pendingDirectViaProxy = directViaProxy;
            _pendingVpnViaProxy = vpnViaProxy;
            _pendingAb = simAb;
            _pendingAc = simAc;
            _pendingAbc = simAbc;
            _pendingMultiFlow = multiFlowOk;
            _pendingConfiguredPermanentVpn = configuredPermanentVpn;
            _pendingInstalledFilters = installedFilters;
            _pendingObservedProbeVpn = observedProbeVpn;
            _pendingObservedProbeDirect = observedProbeDirect;
            _pendingRealVerdict = realVerdict;
            _pendingExtraLines = extraLines;
            _pendingPhases = phases;
            _pendingLog = log;
            _pendingVpnRoutingReady = pre.VpnRoutingReady;
        }
        finally
        {
            engine.SaveConfig(previous with { Rules = ApplicationRulesHelper.GetPermanentApplicationRules(previous).ToList() });
            await engine.RefreshPolicyAsync().ConfigureAwait(false);
        }

        return FinalizeMultiAppRoutingResult(name, engine, permanentVpnBeforeDiagnostic);
    }

    private static IReadOnlyList<MultiAppRoutingRowResult>? _pendingSyntheticRows;
    private static bool _pendingSimultaneous;
    private static bool _pendingCross;
    private static bool _pendingDirectViaProxy;
    private static bool _pendingVpnViaProxy;
    private static bool _pendingAb;
    private static bool _pendingAc;
    private static bool _pendingAbc;
    private static bool _pendingMultiFlow;
    private static int _pendingConfiguredPermanentVpn;
    private static int _pendingInstalledFilters;
    private static int _pendingObservedProbeVpn;
    private static int _pendingObservedProbeDirect;
    private static MultiAppRealAppSpotVerdict _pendingRealVerdict;
    private static List<string>? _pendingExtraLines;
    private static List<MultiAppRoutingPhaseDetail>? _pendingPhases;
    private static List<string>? _pendingLog;
    private static bool _pendingVpnRoutingReady;

    private static DiagnosticResult FinalizeMultiAppRoutingResult(
        string name,
        RouterEngine engine,
        IReadOnlyList<RoutingRule> permanentBefore)
    {
        bool diagnosticCleanupOk = VerifyDiagnosticCleanup(engine, permanentBefore);
        var flags = new MultiAppRoutingSummaryFlags(
            _pendingSimultaneous,
            _pendingCross,
            _pendingDirectViaProxy,
            _pendingVpnViaProxy,
            _pendingAb,
            _pendingAc,
            _pendingAbc,
            _pendingMultiFlow,
            diagnosticCleanupOk);

        string acceptance = MultiAppRoutingCore.FormatAcceptanceReport(
            _pendingSyntheticRows ?? [],
            flags,
            _pendingConfiguredPermanentVpn,
            _pendingInstalledFilters,
            _pendingObservedProbeVpn,
            _pendingObservedProbeDirect,
            _pendingRealVerdict,
            _pendingExtraLines ?? []);

        string phaseReport = MultiAppRoutingCore.FormatPhaseReport(_pendingPhases ?? []);
        string body = string.Join(Environment.NewLine, (_pendingLog ?? []).Prepend(acceptance).Prepend(phaseReport));
        bool pass = MultiAppRoutingCore.EvaluateSyntheticPass(_pendingSyntheticRows ?? [], flags) && _pendingVpnRoutingReady;
        return pass
            ? Pass(name, Bilingual("Multi-app routing isolation verified.\n" + body, "Multi-app izolyaciya OK.\n" + body))
            : Fail(name, Bilingual("Multi-app routing isolation failed.\n" + body, "Multi-app izolyaciya FAIL.\n" + body));
    }

    private static async Task RemoveStaleDiagnosticRulesAsync(
        RouterEngine engine,
        List<string> log,
        CancellationToken ct)
    {
        IReadOnlyList<RoutingRule> stale = ApplicationRulesHelper.GetDiagnosticApplicationRules(engine.Config.Rules);
        if (stale.Count == 0)
        {
            return;
        }

        log.Add("staleDiagnosticRulesRemoved=" + stale.Count);
        foreach (RoutingRule rule in stale)
        {
            log.Add("  removed:" + rule.Name + "|" + rule.Target);
        }

        engine.SaveConfig(ApplicationRulesHelper.WithoutDiagnosticApplicationRules(engine.Config));
        await engine.RefreshPolicyAsync().ConfigureAwait(false);
        _ = ct;
    }

    private static bool VerifyDiagnosticCleanup(RouterEngine engine, IReadOnlyList<RoutingRule> permanentBefore)
    {
        if (ApplicationRulesHelper.GetDiagnosticApplicationRules(engine.Config.Rules).Count > 0)
        {
            return false;
        }

        foreach (RoutingRule rule in permanentBefore)
        {
            bool stillThere = engine.Config.Rules.Any(r =>
                string.Equals(r.Name, rule.Name, StringComparison.OrdinalIgnoreCase)
                && ApplicationRulesHelper.PathsEqual(r.Target, rule.Target));
            if (!stillThere)
            {
                return false;
            }
        }

        return true;
    }
    private static IEnumerable<RoutingRule> StripMultiAppTempRules(IEnumerable<RoutingRule> rules) =>
        rules.Where(r => r.Name is not MultiAppRoutingCore.TempRuleNameA and not MultiAppRoutingCore.TempRuleNameB);
    private static async Task<MultiAppRoutingRowResult> RunMultiAppProbeAsync(
        RouterEngine engine,
        string exe,
        MultiAppRoutingSlot slot,
        MultiAppExpectedRoute expected,
        string url,
        string? directBaseline,
        CancellationToken ct)
    {
        long proxyBefore = engine.Snapshot().ProxyDiagnostics.AcceptedConnections;
        ProbeRunResult run = await RunProbe(exe, ["--http", url], ct).ConfigureAwait(false);
        if (ProbeLaunchFailure(MultiAppRoutingCore.DiagnosticName, run) is not null)
        {
            return new MultiAppRoutingRowResult(slot, expected, MultiAppExpectedRoute.Direct, false);
        }
        bool httpOk = run.Output.Contains("http OK", StringComparison.Ordinal);
        string? local = ParseLocal(run.Output);
        bool wfp = engine.Proxy?.Flows.Any(f =>
            ApplicationRulesHelper.PathsEqual(f.ProcessPath, exe) && f.WfpRedirect) == true;
        bool proxyAccepted = engine.Snapshot().ProxyDiagnostics.AcceptedConnections > proxyBefore;
        MultiAppExpectedRoute actual = MultiAppRoutingCore.ClassifyEgress(httpOk, wfp, proxyAccepted, local, directBaseline);
        return new MultiAppRoutingRowResult(slot, expected, actual, actual == expected);
    }
    private static async Task<IReadOnlyList<MultiAppRoutingRowResult>> RunMultiAppSimultaneousAsync(
        RouterEngine engine,
        string url,
        string? directBaseline,
        string pathA,
        string pathB,
        string pathC,
        IReadOnlyList<MultiAppRoutingSlot> slots,
        CancellationToken ct)
    {
        var tasks = new List<Task<ProbeRunResult>>();
        var slotByPath = new Dictionary<string, MultiAppRoutingSlot>(StringComparer.OrdinalIgnoreCase)
        {
            [pathA] = MultiAppRoutingSlot.A,
            [pathB] = MultiAppRoutingSlot.B,
            [pathC] = MultiAppRoutingSlot.C,
        };
        var expectedBySlot = new Dictionary<MultiAppRoutingSlot, MultiAppExpectedRoute>
        {
            [MultiAppRoutingSlot.A] = MultiAppExpectedRoute.Vpn,
            [MultiAppRoutingSlot.B] = MultiAppExpectedRoute.Vpn,
            [MultiAppRoutingSlot.C] = MultiAppExpectedRoute.Direct,
        };
        foreach (MultiAppRoutingSlot slot in slots)
        {
            string path = slot switch
            {
                MultiAppRoutingSlot.A => pathA,
                MultiAppRoutingSlot.B => pathB,
                _ => pathC,
            };
            tasks.Add(RunProbe(path, ["--http", url], ct));
        }
        long proxyBefore = engine.Snapshot().ProxyDiagnostics.AcceptedConnections;
        ProbeRunResult[] results = await Task.WhenAll(tasks).ConfigureAwait(false);
        _ = proxyBefore;
        var rows = new List<MultiAppRoutingRowResult>();
        for (int i = 0; i < slots.Count; i++)
        {
            MultiAppRoutingSlot slot = slots[i];
            string path = slot switch
            {
                MultiAppRoutingSlot.A => pathA,
                MultiAppRoutingSlot.B => pathB,
                _ => pathC,
            };
            ProbeRunResult run = results[i];
            bool httpOk = run.Launched && run.Output.Contains("http OK", StringComparison.Ordinal);
            string? local = ParseLocal(run.Output);
            bool wfp = engine.Proxy?.Flows.Any(f =>
                ApplicationRulesHelper.PathsEqual(f.ProcessPath, path) && f.WfpRedirect) == true;
            MultiAppExpectedRoute expected = expectedBySlot[slot];
            MultiAppExpectedRoute actual = MultiAppRoutingCore.ClassifyEgress(httpOk, wfp, wfp, local, directBaseline);
            rows.Add(new MultiAppRoutingRowResult(slot, expected, actual, actual == expected));
        }
        return rows;
    }
    private static async Task<bool> RunMultiFlowBurstAsync(
        RouterEngine engine,
        string exe,
        string url,
        string phaseName,
        List<MultiAppRoutingPhaseDetail> phases,
        CancellationToken ct)
    {
        Task<ProbeRunResult>[] burst =
        [
            RunProbe(exe, ["--http", url], ct),
            RunProbe(exe, ["--http", url], ct),
            RunProbe(exe, ["--http", url], ct),
        ];
        ProbeRunResult[] results = await Task.WhenAll(burst).ConfigureAwait(false);
        int httpOk = results.Count(r => r.Launched && r.Output.Contains("http OK", StringComparison.Ordinal));
        bool pass = httpOk >= 2;
        FlowEvent? flow = engine.Snapshot().Flows.LastOrDefault(f => ApplicationRulesHelper.PathsEqual(f.ProcessPath, exe));
        phases.Add(new MultiAppRoutingPhaseDetail(
            phaseName,
            Path.GetFileName(exe),
            MultiAppExpectedRoute.Vpn,
            pass ? MultiAppExpectedRoute.Vpn : MultiAppExpectedRoute.Direct,
            ParseLocal(results.LastOrDefault(r => r.Launched)?.Output ?? ""),
            flow?.WfpRedirect == true,
            flow?.RuleName,
            flow?.Pid ?? 0,
            flow?.ProcessPath,
            pass));
        return pass;
    }
    private static async Task RunRuleIsolationAsync(
        RouterEngine engine,
        AppConfiguration previous,
        string pathA,
        string pathB,
        string url,
        string? directBaseline,
        List<MultiAppRoutingRowResult> rows,
        CancellationToken ct)
    {
        List<RoutingRule> onlyA =
        [
            RoutingRule.Create(RuleType.Application, MultiAppRoutingCore.TempRuleNameA, pathA, RouteMode.Vpn),
        ];
        engine.SaveConfig(previous with { Rules = StripMultiAppTempRules(previous.Rules).Concat(onlyA).ToList() });
        await engine.RefreshPolicyAsync().ConfigureAwait(false);
        rows.Add(await RunMultiAppProbeAsync(engine, pathA, MultiAppRoutingSlot.A, MultiAppExpectedRoute.Vpn, url, directBaseline, ct).ConfigureAwait(false));
        rows.Add(await RunMultiAppProbeAsync(engine, pathB, MultiAppRoutingSlot.B, MultiAppExpectedRoute.Direct, url, directBaseline, ct).ConfigureAwait(false));
        List<RoutingRule> both =
        [
            RoutingRule.Create(RuleType.Application, MultiAppRoutingCore.TempRuleNameA, pathA, RouteMode.Vpn),
            RoutingRule.Create(RuleType.Application, MultiAppRoutingCore.TempRuleNameB, pathB, RouteMode.Vpn),
        ];
        engine.SaveConfig(previous with { Rules = StripMultiAppTempRules(previous.Rules).Concat(both).ToList() });
        await engine.RefreshPolicyAsync().ConfigureAwait(false);
    }
    private static async Task RunAppRestartIsolationAsync(
        RouterEngine engine,
        string pathA,
        string pathB,
        string url,
        CancellationToken ct)
    {
        Task<ProbeRunResult> b = RunProbe(pathB, ["--http", url], ct);
        Task<ProbeRunResult> a1 = RunProbe(pathA, ["--http", url], ct);
        await Task.WhenAll(b, a1).ConfigureAwait(false);
        _ = await RunProbe(pathA, ["--http", url], ct).ConfigureAwait(false);
    }
    private static void RecordRowAsPhase(
        List<MultiAppRoutingPhaseDetail> phases,
        RouterEngine engine,
        string phaseName,
        string exe,
        MultiAppRoutingRowResult row,
        string? directBaseline)
    {
        FlowEvent? flow = FindLatestProbeFlow(engine, exe);
        phases.Add(new MultiAppRoutingPhaseDetail(
            phaseName,
            row.SlotLabel,
            row.Expected,
            row.Actual,
            null,
            flow?.WfpRedirect == true || flow?.ProxyAccepted == true,
            flow?.RuleName,
            flow?.Pid ?? 0,
            flow?.ProcessPath ?? exe,
            row.Pass));
    }
    private static void RecordSimultaneousPhases(
        List<MultiAppRoutingPhaseDetail> phases,
        RouterEngine engine,
        string phaseName,
        IReadOnlyList<MultiAppRoutingRowResult> simRows,
        string pathA,
        string pathB,
        string pathC,
        string? directBaseline)
    {
        foreach (MultiAppRoutingRowResult row in simRows)
        {
            string exe = row.Slot switch
            {
                MultiAppRoutingSlot.A => pathA,
                MultiAppRoutingSlot.B => pathB,
                _ => pathC,
            };
            RecordRowAsPhase(phases, engine, phaseName, exe, row, directBaseline);
        }
    }
    private static FlowEvent? FindLatestProbeFlow(RouterEngine engine, string exe) =>
        engine.Snapshot().Flows.LastOrDefault(f => ApplicationRulesHelper.PathsEqual(f.ProcessPath, exe));
    private static List<string> BuildInstalledFilterBreakdown(RouterEngine engine, ServiceSnapshot snap)
    {
        var lines = new List<string> { "--- installed app filters ---" };
        foreach (RoutingRule rule in engine.Config.Rules.Where(r => r.Type == RuleType.Application && r.Enabled))
        {
            string kind = MultiAppRoutingCore.IsTempDiagnosticRuleName(rule.Name) ? "temporary-diagnostic"
                : ApplicationRulesHelper.IsDiagnosticApplicationRule(rule) ? "diagnostic"
                : "production";
            var filter = WfpPolicyHealth.FindCalloutFilter(snap.WfpPolicy, rule.Target);
            lines.Add($"{rule.Name} | {rule.Target} | {kind} | filter={(filter?.FilterId.ToString() ?? "-")}");
        }
        return lines;
    }
    private static async Task<(MultiAppRealAppSpotVerdict verdict, List<string> notes)> RunRealAppSpotCheckAsync(
        RouterEngine engine,
        IReadOnlyList<RoutingRule> permanentVpnBeforeDiagnostic,
        string url,
        string? directBaseline,
        CancellationToken ct)
    {
        _ = url;
        var notes = new List<string>();
        List<RoutingRule> candidates = permanentVpnBeforeDiagnostic.Take(2).ToList();
        if (candidates.Count == 0)
        {
            notes.Add("realApps=skipped (no permanent VPN apps before diagnostic temp rules)");
            return (MultiAppRealAppSpotVerdict.Skipped, notes);
        }
        bool anyObserved = false;
        bool anyFail = false;
        foreach (RoutingRule rule in candidates)
        {
            if (string.IsNullOrWhiteSpace(rule.Target) || !File.Exists(rule.Target))
            {
                notes.Add("missing:" + rule.Name);
                continue;
            }
            int before = engine.Snapshot().Flows.Count(f => ApplicationRulesHelper.PathsEqual(f.ProcessPath, rule.Target));
            await Task.Delay(1200, ct).ConfigureAwait(false);
            List<FlowEvent> after = engine.Snapshot().Flows
                .Where(f => ApplicationRulesHelper.PathsEqual(f.ProcessPath, rule.Target))
                .ToList();
            if (after.Count <= before)
            {
                notes.Add(rule.Name + "=OBSERVED-NO-TRAFFIC");
                continue;
            }
            anyObserved = true;
            FlowEvent flow = after[^1];
            bool viaProxy = flow.WfpRedirect || flow.ProxyAccepted;
            MultiAppExpectedRoute actual = MultiAppRoutingCore.ClassifyEgress(true, flow.WfpRedirect, viaProxy, null, directBaseline);
            bool ok = actual == MultiAppExpectedRoute.Vpn;
            notes.Add(rule.Name + "=" + (actual == MultiAppExpectedRoute.Vpn ? "VPN" : "DIRECT") + (ok ? "(ok)" : "(fail)"));
            if (!ok)
            {
                anyFail = true;
            }
        }
        if (anyFail)
        {
            return (MultiAppRealAppSpotVerdict.Fail, notes);
        }
        if (!anyObserved)
        {
            return (MultiAppRealAppSpotVerdict.ObservedNoTraffic, notes);
        }
        return (MultiAppRealAppSpotVerdict.Pass, notes);
    }
    private static async Task<string?> ReadLocalIpAsync(string exe, string url, CancellationToken ct)
    {
        ProbeRunResult run = await RunProbe(exe, ["--http", url], ct).ConfigureAwait(false);
        return ParseLocal(run.Output);
    }
    private static int CountDistinctPaths(IEnumerable<FlowEvent> flows)
    {
        HashSet<string> set = new(StringComparer.OrdinalIgnoreCase);
        foreach (FlowEvent flow in flows)
        {
            if (!string.IsNullOrWhiteSpace(flow.ProcessPath))
            {
                set.Add(flow.ProcessPath);
            }
        }
        return set.Count;
    }
}
